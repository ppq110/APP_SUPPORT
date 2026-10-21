using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace GopCadApp;

internal enum CadMergeMode
{
    StandaloneDwg,
    XrefPackage
}

internal enum DwgSourceKind
{
    DirectMerge,
    ExportLayoutFirst
}

internal sealed record DwgSourceItem(string Path, DwgSourceKind Kind);

// MasterDwgPath là nơi AutoCAD lưu master (luôn là đường dẫn ASCII, xem ResolveWorkRoot);
// FinalMasterDwgPath là nơi người dùng muốn nhận kết quả (thư mục xuất). Hai đường dẫn này
// giống nhau trừ khi thư mục xuất có ký tự không phải ASCII (vd. dấu tiếng Việt).
// Sources: danh sách file đã gộp, đúng thứ tự Sheet_001, Sheet_002... / CAD_0001, CAD_0002...
internal sealed record DwgPackResult(
    string PackageFolder,
    string CadFolder,
    string MasterDwgPath,
    string InputDrawingPath,
    string ConsoleLogPath,
    string ScriptPath,
    string LispPath,
    string BatchPath,
    string ZipPath,
    int DwgCount,
    string FinalMasterDwgPath,
    IReadOnlyList<DwgSourceItem> Sources);

internal sealed record CadProgress(int Current, int Total, string Stage, string FileName);

internal static class DwgPackager
{
    private static readonly TimeSpan AutoCadProcessTimeout = TimeSpan.FromMinutes(30);
    private const int SwHide = 0;
    private const int SwMinimize = 6;

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private static readonly string[] AcadCandidates =
    [
        @"C:\Program Files\Autodesk\AutoCAD 2026\accoreconsole.exe",
        @"C:\Program Files\Autodesk\AutoCAD 2025\accoreconsole.exe",
        @"C:\Program Files\Autodesk\AutoCAD 2024\accoreconsole.exe",
        @"C:\Program Files\Autodesk\AutoCAD 2023\accoreconsole.exe",
        @"C:\Program Files\Autodesk\AutoCAD 2022\accoreconsole.exe",
        @"C:\Program Files\Autodesk\AutoCAD 2021\accoreconsole.exe",
        @"C:\Program Files\Autodesk\AutoCAD 2020\accoreconsole.exe"
    ];

    public static string? FindAutoCadCoreConsole()
    {
        string? fixedCandidate = AcadCandidates.FirstOrDefault(File.Exists);
        if (fixedCandidate is not null)
        {
            return fixedCandidate;
        }

        List<string> roots = [];
        AddIfExists(roots, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        AddIfExists(roots, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));

        foreach (string root in roots)
        {
            string autodeskRoot = Path.Combine(root, "Autodesk");
            if (!Directory.Exists(autodeskRoot))
            {
                continue;
            }

            string? found = SafeEnumerateFiles(autodeskRoot, "accoreconsole.exe")
                .OrderByDescending(GetVersionScore)
                .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private static void AddIfExists(List<string> values, string path)
    {
        if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path) && !values.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            values.Add(path);
        }
    }

    private static IEnumerable<string> SafeEnumerateFiles(string root, string searchPattern)
    {
        Queue<string> pending = new();
        pending.Enqueue(root);

        while (pending.Count > 0)
        {
            string current = pending.Dequeue();

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(current, searchPattern, SearchOption.TopDirectoryOnly).ToList();
            }
            catch
            {
                files = [];
            }

            foreach (string file in files)
            {
                yield return file;
            }

            IEnumerable<string> directories;
            try
            {
                directories = Directory.EnumerateDirectories(current).ToList();
            }
            catch
            {
                directories = [];
            }

            foreach (string directory in directories)
            {
                pending.Enqueue(directory);
            }
        }
    }

    private static int GetVersionScore(string path)
    {
        Match match = System.Text.RegularExpressions.Regex.Match(path, @"20\d{2}");
        return match.Success && int.TryParse(match.Value, out int year) ? year : 0;
    }

    // AutoCAD tự tạo file .bak (bản lưu cũ) cạnh mỗi .dwg. Nếu trong danh sách đã có X.dwg thì bỏ
    // X.bak cùng thư mục, cùng tên: gộp cả hai sẽ ra 2 bản của cùng một bản vẽ (cả bản cũ lẫn bản mới).
    // File .bak không có .dwg đi kèm (vd. đã đổi tên) vẫn được giữ lại.
    public static List<string> RemoveBackupDuplicates(IEnumerable<string> paths, out int removedCount)
    {
        List<string> all = paths.ToList();
        HashSet<string> dwgKeys = new(StringComparer.OrdinalIgnoreCase);
        foreach (string path in all)
        {
            if (Path.GetExtension(path).Equals(".dwg", StringComparison.OrdinalIgnoreCase))
            {
                dwgKeys.Add(Path.ChangeExtension(path, null));
            }
        }

        List<string> kept = all
            .Where(path => !Path.GetExtension(path).Equals(".bak", StringComparison.OrdinalIgnoreCase)
                           || !dwgKeys.Contains(Path.ChangeExtension(path, null)))
            .ToList();

        removedCount = all.Count - kept.Count;
        return kept;
    }

    public static DwgPackResult CreatePackage(
        IReadOnlyList<DwgSourceItem> sourceItems,
        string outputRoot,
        string? accoreConsolePath,
        CadMergeMode mergeMode,
        IProgress<CadProgress>? progress = null)
    {
        if (sourceItems.Count == 0)
        {
            throw new InvalidOperationException("Chưa chọn file DWG nào để gộp.");
        }

        if (string.IsNullOrWhiteSpace(outputRoot))
        {
            throw new InvalidOperationException("Chưa chọn thư mục xuất kết quả.");
        }

        outputRoot = Path.GetFullPath(outputRoot.Trim());

        List<DwgSourceItem> validItems = sourceItems
            .Where(item => File.Exists(item.Path))
            .Where(item => IsCadDrawingSource(item.Path))
            .Where(item => !Path.GetFileName(item.Path).StartsWith("GOP_CAD_MASTER", StringComparison.OrdinalIgnoreCase))
            .DistinctBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item.Path, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (validItems.Count == 0)
        {
            throw new InvalidOperationException("Không có file .dwg/.bak nào để gộp.");
        }

        Directory.CreateDirectory(outputRoot);
        progress?.Report(new CadProgress(0, validItems.Count, "Tìm thấy DWG", $"{validItems.Count} file"));

        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        string workRoot = ResolveWorkRoot(outputRoot);
        string packageFolder = Path.Combine(workRoot, "_GOP_CAD_WORK_" + timestamp);
        string cadFolder = Path.Combine(packageFolder, "CAD_LINKS");
        Directory.CreateDirectory(cadFolder);

        List<string> preparedDwgs = PrepareSourceDwgs(validItems, packageFolder, accoreConsolePath, progress);
        List<string> copiedDwgs = CopyDwgs(packageFolder, cadFolder, preparedDwgs, false, progress);
        CleanupCopiedDwgsForMerge(copiedDwgs, validItems.Select(item => Path.GetFileName(item.Path)).ToList(), accoreConsolePath, packageFolder, progress);

        string masterName = "GOP_CAD_MASTER_" + timestamp + ".dwg";
        string masterDwgPath = Path.Combine(workRoot, masterName);
        string finalMasterDwgPath = Path.Combine(outputRoot, masterName);
        string inputDrawingPath = Path.Combine(packageFolder, "blank_input.dxf");
        string consoleLogPath = Path.Combine(packageFolder, "AUTOCAD_CONSOLE_LOG.txt");
        string scriptName = mergeMode == CadMergeMode.StandaloneDwg ? "tao_master_doc_lap.scr" : "tao_master_xref.scr";
        string batchName = mergeMode == CadMergeMode.StandaloneDwg ? "chay_tao_master_doc_lap.bat" : "chay_tao_master_xref.bat";
        string scriptPath = Path.Combine(packageFolder, scriptName);
        string lispPath = Path.Combine(packageFolder, "gopcad_merge_layouts.lsp");
        string batchPath = Path.Combine(packageFolder, batchName);

        // Tên hiển thị trên thanh tiến trình: tên file gốc bỏ dấu (AutoCAD chỉ in ra ASCII được an toàn).
        List<string> displayNames = validItems.Select(item => ToAsciiDisplayName(item.Path)).ToList();

        File.WriteAllText(inputDrawingPath, BuildBlankDxf(), Encoding.ASCII);
        File.WriteAllText(lispPath, BuildAutoCadLispSafe(copiedDwgs, displayNames, masterDwgPath, mergeMode), Encoding.Default);
        File.WriteAllText(scriptPath, BuildAutoCadScript(lispPath), Encoding.Default);
        File.WriteAllText(batchPath, BuildBatch(scriptPath, inputDrawingPath), Encoding.Default);
        File.WriteAllText(Path.Combine(packageFolder, "HUONG_DAN.txt"), BuildInstructions(masterDwgPath, scriptPath, batchPath, packageFolder + ".zip", mergeMode), Encoding.UTF8);

        return new DwgPackResult(packageFolder, cadFolder, masterDwgPath, inputDrawingPath, consoleLogPath, scriptPath, lispPath, batchPath, packageFolder + ".zip", copiedDwgs.Count, finalMasterDwgPath, validItems);
    }

    // Gọi sau khi AutoCAD đã tạo xong master: đưa master ra thư mục xuất, ghi file danh sách đối chiếu
    // (Sheet_001 <-> tên file gốc) rồi xoá thư mục làm việc. ListPath = null nếu không ghi được danh sách.
    public static (string MasterPath, string? ListPath) PublishResult(DwgPackResult result)
    {
        string masterPath = result.FinalMasterDwgPath;
        if (!string.Equals(result.MasterDwgPath, masterPath, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                File.Move(result.MasterDwgPath, masterPath, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new InvalidOperationException(
                    $"Đã tạo xong master nhưng không chuyển được ra thư mục xuất. File đang nằm ở: {result.MasterDwgPath}{Environment.NewLine}{ex.Message}",
                    ex);
            }
        }

        string? listPath = Path.ChangeExtension(masterPath, null) + "_DANH_SACH.csv";
        try
        {
            // UTF-8 có BOM để Excel hiển thị đúng tiếng Việt.
            File.WriteAllText(listPath, BuildMappingCsv(result.Sources), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            listPath = null;
        }

        DeleteWorkingFolder(result.PackageFolder);
        return (masterPath, listPath);
    }

    private static string BuildMappingCsv(IReadOnlyList<DwgSourceItem> sources)
    {
        StringBuilder csv = new();
        csv.AppendLine("Layout (Sheet),Block (CAD),Tên file gốc,Đường dẫn gốc,Cách xử lý");
        for (int i = 0; i < sources.Count; i++)
        {
            DwgSourceItem item = sources[i];
            string kind = item.Kind == DwgSourceKind.ExportLayoutFirst ? "Loại 2 - Export layout trước" : "Loại 1 - Gộp trực tiếp";
            csv.AppendLine(CultureInfo.InvariantCulture,
                $"Sheet_{i + 1:000},CAD_{i + 1:0000},{CsvField(Path.GetFileName(item.Path))},{CsvField(item.Path)},{CsvField(kind)}");
        }

        return csv.ToString();
    }

    private static string CsvField(string value)
    {
        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    // Tên file gốc bỏ dấu tiếng Việt, chỉ giữ ký tự ASCII in được, để in lên log/thanh tiến trình của AutoCAD.
    private static string ToAsciiDisplayName(string path)
    {
        string normalized = Path.GetFileName(path).Normalize(NormalizationForm.FormD);
        StringBuilder name = new();
        foreach (char c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            char mapped = c switch { 'đ' => 'd', 'Đ' => 'D', _ => c };
            name.Append(mapped is >= ' ' and <= '~' and not ('|' or '"' or '\\') ? mapped : '_');
        }

        return name.Length > 80 ? name.ToString(0, 80) : name.ToString();
    }

    private static bool IsAscii(string value)
    {
        return value.All(c => c < 128);
    }

    // AutoCAD đọc script/lisp theo bảng mã ANSI nhưng app ghi file bằng UTF-8 (Encoding.Default trên .NET 8),
    // nên đường dẫn có dấu tiếng Việt (vd. C:\Users\Nguyễn\Desktop) có thể bị đọc sai. Khi thư mục xuất có ký tự
    // không phải ASCII, AutoCAD làm việc trong một thư mục ASCII (ProgramData...) và master được chuyển ra
    // thư mục xuất sau cùng (PublishResult). Không tìm được thư mục ASCII thì dùng luôn thư mục xuất như cũ.
    private static string ResolveWorkRoot(string outputRoot)
    {
        if (IsAscii(outputRoot))
        {
            return outputRoot;
        }

        string? publicRoot = Environment.GetEnvironmentVariable("PUBLIC");
        string?[] candidates =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "BIMLab", "DwgMerge"),
            string.IsNullOrEmpty(publicRoot) ? null : Path.Combine(publicRoot, "BIMLab", "DwgMerge"),
            Path.Combine(Path.GetTempPath(), "BIMLab_DwgMerge")
        ];

        foreach (string? candidate in candidates)
        {
            if (candidate is not null && IsAscii(candidate) && TryUseFolder(candidate))
            {
                return candidate;
            }
        }

        return outputRoot;
    }

    private static bool TryUseFolder(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            string probe = Path.Combine(folder, ".write_" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(probe, "x");
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private static List<string> PrepareSourceDwgs(
        IReadOnlyList<DwgSourceItem> sourceItems,
        string packageFolder,
        string? accoreConsolePath,
        IProgress<CadProgress>? progress)
    {
        List<string> prepared = [];
        int exportTotal = sourceItems.Count(item => item.Kind == DwgSourceKind.ExportLayoutFirst);
        int exportIndex = 0;

        if (exportTotal > 0 && string.IsNullOrWhiteSpace(accoreConsolePath))
        {
            throw new InvalidOperationException("Có file Loại 2 cần export layout trước, nhưng không tìm thấy AutoCAD Core Console. Hãy kiểm tra máy đã cài AutoCAD.");
        }

        string exportFolder = Path.Combine(packageFolder, "EXPORTED_LAYOUTS");
        Directory.CreateDirectory(exportFolder);

        for (int index = 0; index < sourceItems.Count; index++)
        {
            DwgSourceItem item = sourceItems[index];
            if (item.Kind == DwgSourceKind.DirectMerge)
            {
                prepared.Add(item.Path);
                continue;
            }

            exportIndex++;
            progress?.Report(new CadProgress(exportIndex, exportTotal, "Đang export layout", Path.GetFileName(item.Path)));
            string outputPath = Path.Combine(exportFolder, $"EXPORTED_{index + 1:0000}.dwg");
            ExportLayoutToDwg(item.Path, outputPath, accoreConsolePath!, packageFolder, index + 1);
            progress?.Report(new CadProgress(exportIndex, exportTotal, "Đã export layout", Path.GetFileName(outputPath)));
            prepared.Add(outputPath);
        }

        return prepared;
    }

    private static void ExportLayoutToDwg(
        string sourceDwgPath,
        string outputDwgPath,
        string accoreConsolePath,
        string packageFolder,
        int index)
    {
        if (!File.Exists(accoreConsolePath))
        {
            throw new FileNotFoundException("Không tìm thấy accoreconsole.exe.", accoreConsolePath);
        }

        if (File.Exists(outputDwgPath))
        {
            File.Delete(outputDwgPath);
        }

        string exportFolder = Path.Combine(packageFolder, "EXPORT_SCRIPT");
        Directory.CreateDirectory(exportFolder);
        string inputCopyPath = Path.Combine(exportFolder, $"export_layout_{index:0000}_input.dwg");
        string lispPath = Path.Combine(exportFolder, $"export_layout_{index:0000}.lsp");
        string scriptPath = Path.Combine(exportFolder, $"export_layout_{index:0000}.scr");
        string logPath = Path.Combine(exportFolder, $"export_layout_{index:0000}_log.txt");

        File.Copy(sourceDwgPath, inputCopyPath, overwrite: true);
        string? fullAutoCadPath = FindFullAutoCadFromCoreConsole(accoreConsolePath);
        bool useFullAutoCad = fullAutoCadPath is not null;
        File.WriteAllText(lispPath, useFullAutoCad ? BuildExportLayoutCommandLisp(outputDwgPath) : BuildExportLayoutLisp(outputDwgPath), Encoding.Default);
        File.WriteAllText(scriptPath, BuildExportLayoutScript(lispPath), Encoding.Default);

        ProcessStartInfo startInfo = new()
        {
            FileName = fullAutoCadPath ?? accoreConsolePath,
            Arguments = useFullAutoCad
                ? $"\"{inputCopyPath}\" /b \"{scriptPath}\" /nologo"
                : $"/i \"{inputCopyPath}\" /s \"{scriptPath}\"",
            WorkingDirectory = exportFolder,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        StringBuilder output = new();
        StringBuilder error = new();
        using Process process = new() { StartInfo = startInfo };
        process.OutputDataReceived += (_, args) =>
        {
            string? line = NormalizeConsoleLine(args.Data);
            if (!string.IsNullOrWhiteSpace(line))
            {
                output.AppendLine(line);
            }
        };
        process.ErrorDataReceived += (_, args) =>
        {
            string? line = NormalizeConsoleLine(args.Data);
            if (!string.IsNullOrWhiteSpace(line))
            {
                error.AppendLine(line);
            }
        };

        if (!process.Start())
        {
            throw new InvalidOperationException("Không khởi động được AutoCAD Core Console để export layout.");
        }

        HideAutoCadWindow(process, useFullAutoCad);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        bool killedAfterOutput = WaitForAutoCadProcess(process, outputDwgPath, output, "GOPCAD_EXPORT_ERROR");
        if (!process.HasExited)
        {
            TryKillProcess(process);
            output.AppendLine($"GOPCAD_EXPORT_ERROR|AutoCAD Core Console qua thoi gian cho {AutoCadProcessTimeout.TotalMinutes:0} phut");
        }

        process.WaitForExit();
        WriteConsoleLog(logPath, output, error);

        string combinedLog = output + Environment.NewLine + error;
        if ((!killedAfterOutput && process.ExitCode != 0) || HasAutoCadFailure(combinedLog) || !File.Exists(outputDwgPath))
        {
            throw new InvalidOperationException($"Không export được layout từ file {Path.GetFileName(sourceDwgPath)}. Log: {logPath}{Environment.NewLine}{GetLogTail(combinedLog)}");
        }

        CleanupExportedLayoutDwg(outputDwgPath, accoreConsolePath, exportFolder, index);
    }

    public static bool IsCadDrawingSource(string path)
    {
        string extension = Path.GetExtension(path);
        return extension.Equals(".dwg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".bak", StringComparison.OrdinalIgnoreCase);
    }

    private static void CleanupExportedLayoutDwg(
        string exportedDwgPath,
        string accoreConsolePath,
        string exportFolder,
        int index)
    {
        string lispPath = Path.Combine(exportFolder, $"cleanup_exported_layout_{index:0000}.lsp");
        string scriptPath = Path.Combine(exportFolder, $"cleanup_exported_layout_{index:0000}.scr");
        string logPath = Path.Combine(exportFolder, $"cleanup_exported_layout_{index:0000}_log.txt");

        File.WriteAllText(lispPath, BuildCleanupExportedLayoutLisp(), Encoding.Default);
        File.WriteAllText(scriptPath, BuildCleanupExportedLayoutScript(lispPath), Encoding.Default);

        ProcessStartInfo startInfo = new()
        {
            FileName = accoreConsolePath,
            Arguments = $"/i \"{exportedDwgPath}\" /s \"{scriptPath}\"",
            WorkingDirectory = exportFolder,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        StringBuilder output = new();
        StringBuilder error = new();
        using Process process = new() { StartInfo = startInfo };
        process.OutputDataReceived += (_, args) =>
        {
            string? line = NormalizeConsoleLine(args.Data);
            if (!string.IsNullOrWhiteSpace(line))
            {
                output.AppendLine(line);
            }
        };
        process.ErrorDataReceived += (_, args) =>
        {
            string? line = NormalizeConsoleLine(args.Data);
            if (!string.IsNullOrWhiteSpace(line))
            {
                error.AppendLine(line);
            }
        };

        if (!process.Start())
        {
            throw new InvalidOperationException("Khong khoi dong duoc AutoCAD Core Console de don viewport sau export layout.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        if (!process.WaitForExit(AutoCadProcessTimeout))
        {
            TryKillProcess(process);
            output.AppendLine($"GOPCAD_CLEANUP_ERROR|AutoCAD Core Console qua thoi gian cho {AutoCadProcessTimeout.TotalMinutes:0} phut");
        }

        process.WaitForExit();
        WriteConsoleLog(logPath, output, error);

        string combinedLog = output + Environment.NewLine + error;
        if (process.ExitCode != 0 || HasAutoCadFailure(combinedLog))
        {
            throw new InvalidOperationException($"Khong don duoc khung viewport sau khi export layout. Log: {logPath}{Environment.NewLine}{GetLogTail(combinedLog)}");
        }
    }

    public static async Task RunAutoCadScriptAsync(
        string accoreConsolePath,
        string inputDrawingPath,
        string scriptPath,
        string consoleLogPath,
        string? expectedOutputPath = null,
        IProgress<CadProgress>? progress = null,
        Action<string>? logLine = null)
    {
        if (!File.Exists(accoreConsolePath))
        {
            throw new FileNotFoundException("Không tìm thấy accoreconsole.exe.", accoreConsolePath);
        }
        if (!File.Exists(inputDrawingPath))
        {
            throw new FileNotFoundException("Không tìm thấy file DWG/DXF nền để chạy AutoCAD Core Console.", inputDrawingPath);
        }

        ProcessStartInfo startInfo = new()
        {
            FileName = accoreConsolePath,
            Arguments = $"/i \"{inputDrawingPath}\" /s \"{scriptPath}\"",
            WorkingDirectory = Path.GetDirectoryName(scriptPath) ?? Environment.CurrentDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        StringBuilder output = new();
        StringBuilder error = new();
        using Process process = new() { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, args) =>
        {
            string? line = NormalizeConsoleLine(args.Data);
            if (string.IsNullOrWhiteSpace(line))
            {
                return;
            }

            output.AppendLine(line);
            CadProgress? parsed = ParseAutoCadProgress(line);
            if (parsed is not null)
            {
                progress?.Report(parsed);
                SafeLog(logLine, $"{parsed.Stage}: {parsed.Current}/{parsed.Total} - {parsed.FileName}");
            }
        };
        process.ErrorDataReceived += (_, args) =>
        {
            string? line = NormalizeConsoleLine(args.Data);
            if (!string.IsNullOrWhiteSpace(line))
            {
                error.AppendLine(line);
            }
        };

        if (!process.Start())
        {
            throw new InvalidOperationException("Không khởi động được AutoCAD Core Console.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        bool killedAfterOutput = await WaitForAutoCadProcessAsync(process, expectedOutputPath, output, "GOPCAD_ERROR");
        WriteConsoleLog(consoleLogPath, output, error);

        string combinedLog = output + Environment.NewLine + error;
        bool outputExists = !string.IsNullOrWhiteSpace(expectedOutputPath) && File.Exists(expectedOutputPath);

        // Lỗi do chính script báo (GOPCAD_*_ERROR|) luôn là thất bại. Các dấu hiệu chung chung của AutoCAD
        // (*Invalid*, "; error:", "Can't find file") có thể xuất hiện ngay cả khi master vẫn tạo ra bình thường,
        // nên nếu file master đã có thì chỉ cảnh báo, không báo thất bại.
        bool scriptReportedError = HasScriptReportedError(combinedLog);
        bool genericFailureMarker = HasAutoCadFailure(combinedLog);
        if ((!killedAfterOutput && process.ExitCode != 0) || scriptReportedError || (genericFailureMarker && !outputExists))
        {
            throw new InvalidOperationException("AutoCAD Core Console báo lỗi." + Environment.NewLine + GetLogTail(combinedLog));
        }

        if (!string.IsNullOrWhiteSpace(expectedOutputPath) && !outputExists)
        {
            throw new InvalidOperationException($"AutoCAD chạy xong nhưng chưa tạo file DWG kết quả: {expectedOutputPath}{Environment.NewLine}{GetLogTail(combinedLog)}");
        }

        if (genericFailureMarker)
        {
            SafeLog(logLine, $"CẢNH BÁO: AutoCAD có in thông báo lỗi nhưng vẫn tạo được master. Nên kiểm tra lại các layout. Log: {consoleLogPath}");
        }
    }

    // logLine được gọi từ luồng đọc output của AutoCAD (không phải luồng giao diện): không để lỗi trong
    // callback thoát ra ngoài, vì exception ở luồng nền sẽ làm sập cả ứng dụng.
    private static void SafeLog(Action<string>? logLine, string message)
    {
        try
        {
            logLine?.Invoke(message);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            // Form đã đóng hoặc chưa sẵn sàng nhận log: bỏ qua dòng log này.
        }
    }

    private static bool HasScriptReportedError(string log)
    {
        return log.Contains("GOPCAD_ERROR|", StringComparison.OrdinalIgnoreCase)
            || log.Contains("GOPCAD_EXPORT_ERROR|", StringComparison.OrdinalIgnoreCase)
            || log.Contains("GOPCAD_CLEANUP_ERROR|", StringComparison.OrdinalIgnoreCase);
    }

    public static void DeleteWorkingFolder(string packageFolder)
    {
        if (Directory.Exists(packageFolder))
        {
            Directory.Delete(packageFolder, recursive: true);
        }
    }

    private static List<string> CopyDwgs(
        string sourceFolder,
        string cadFolder,
        List<string> sourceDwgs,
        bool keepSubfolders,
        IProgress<CadProgress>? progress)
    {
        List<string> copied = [];
        StringBuilder manifest = new();
        manifest.AppendLine("CopiedName,OriginalPath");

        for (int index = 0; index < sourceDwgs.Count; index++)
        {
            string sourceDwg = sourceDwgs[index];
            string displayPath = keepSubfolders ? Path.GetRelativePath(sourceFolder, sourceDwg) : Path.GetFileName(sourceDwg);
            string copiedName = $"CAD_{index + 1:0000}.dwg";

            string destination = Path.Combine(cadFolder, copiedName);
            Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? cadFolder);
            File.Copy(sourceDwg, destination, overwrite: true);
            copied.Add(destination);
            manifest.AppendLine($"{copiedName},\"{sourceDwg.Replace("\"", "\"\"", StringComparison.Ordinal)}\"");
            progress?.Report(new CadProgress(index + 1, sourceDwgs.Count, "Đang copy", displayPath));
        }

        File.WriteAllText(Path.Combine(cadFolder, "_manifest.csv"), manifest.ToString(), Encoding.UTF8);
        return copied;
    }

    private static void CleanupCopiedDwgsForMerge(
        List<string> copiedDwgs,
        IReadOnlyList<string> originalNames,
        string? accoreConsolePath,
        string packageFolder,
        IProgress<CadProgress>? progress)
    {
        if (copiedDwgs.Count == 0)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(accoreConsolePath) || !File.Exists(accoreConsolePath))
        {
            throw new InvalidOperationException("Không tìm thấy AutoCAD Core Console để dọn viewport trước khi gộp.");
        }

        string cleanupFolder = Path.Combine(packageFolder, "MERGE_CLEANUP_SCRIPT");
        Directory.CreateDirectory(cleanupFolder);

        for (int index = 0; index < copiedDwgs.Count; index++)
        {
            string dwgPath = copiedDwgs[index];
            // Hiện tên file GỐC (không phải CAD_0001.dwg là tên bản sao làm việc).
            progress?.Report(new CadProgress(index + 1, copiedDwgs.Count, "Đang dọn viewport", originalNames[index]));
            CleanupExportedLayoutDwg(dwgPath, accoreConsolePath, cleanupFolder, index + 1);
        }
    }

    private static string BuildAutoCadScript(string lispPath)
    {
        StringBuilder script = new();
        script.AppendLine("SECURELOAD");
        script.AppendLine("0");
        script.AppendLine("TRUSTEDPATHS");
        script.AppendLine(QuoteForAutoCad(Path.GetDirectoryName(lispPath) ?? string.Empty));
        script.AppendLine($"(load {ToLispString(lispPath)})");
        script.AppendLine("(gopcad-main)");
        return script.ToString();
    }

    private static string BuildExportLayoutScript(string lispPath)
    {
        StringBuilder script = new();
        script.AppendLine("SECURELOAD");
        script.AppendLine("0");
        script.AppendLine("TRUSTEDPATHS");
        script.AppendLine(QuoteForAutoCad(Path.GetDirectoryName(lispPath) ?? string.Empty));
        script.AppendLine($"(load {ToLispString(lispPath)})");
        script.AppendLine("(gopcad-export-layout)");
        return script.ToString();
    }

    private static string BuildCleanupExportedLayoutScript(string lispPath)
    {
        StringBuilder script = new();
        script.AppendLine("SECURELOAD");
        script.AppendLine("0");
        script.AppendLine("TRUSTEDPATHS");
        script.AppendLine(QuoteForAutoCad(Path.GetDirectoryName(lispPath) ?? string.Empty));
        script.AppendLine($"(load {ToLispString(lispPath)})");
        script.AppendLine("(gopcad-cleanup-exported-layout)");
        return script.ToString();
    }

    private static string BuildCleanupExportedLayoutLisp()
    {
        StringBuilder lisp = new();
        lisp.AppendLine("(vl-load-com)");
        lisp.AppendLine("(defun gopcad-cleanup-exported-layout (/ old-filedia old-cmdecho ss idx ent data layer etype deleted)");
        lisp.AppendLine("  (setq old-filedia (getvar \"FILEDIA\"))");
        lisp.AppendLine("  (setq old-cmdecho (getvar \"CMDECHO\"))");
        lisp.AppendLine("  (setvar \"FILEDIA\" 0)");
        lisp.AppendLine("  (setvar \"CMDECHO\" 0)");
        lisp.AppendLine("  (vl-catch-all-apply 'vl-cmdf (list \"_.-LAYER\" \"_Unlock\" \"*\" \"\"))");
        lisp.AppendLine("  (if (tblsearch \"LAYER\" \"0\") (setvar \"CLAYER\" \"0\"))");
        lisp.AppendLine("  (setq deleted 0)");
        lisp.AppendLine("  (setq ss (ssget \"_X\"))");
        lisp.AppendLine("  (if ss");
        lisp.AppendLine("    (progn");
        lisp.AppendLine("      (setq idx 0)");
        lisp.AppendLine("      (repeat (sslength ss)");
        lisp.AppendLine("        (setq ent (ssname ss idx))");
        lisp.AppendLine("        (setq data (entget ent))");
        lisp.AppendLine("        (setq layer (strcase (cdr (assoc 8 data))))");
        lisp.AppendLine("        (setq etype (strcase (cdr (assoc 0 data))))");
        lisp.AppendLine("        (if (or (= etype \"VIEWPORT\") (wcmatch layer \"*VIEWPORT*\") (wcmatch layer \"*VPORT*\"))");
        lisp.AppendLine("          (if (not (vl-catch-all-error-p (vl-catch-all-apply 'entdel (list ent))))");
        lisp.AppendLine("            (setq deleted (1+ deleted))");
        lisp.AppendLine("          )");
        lisp.AppendLine("        )");
        lisp.AppendLine("        (setq idx (1+ idx))");
        lisp.AppendLine("      )");
        lisp.AppendLine("    )");
        lisp.AppendLine("  )");
        lisp.AppendLine("  (princ (strcat \"\\nGOPCAD_CLEANUP_VIEWPORT|\" (itoa deleted)))");
        lisp.AppendLine("  (vl-catch-all-apply 'vl-cmdf (list \"_.QSAVE\"))");
        lisp.AppendLine("  (setvar \"FILEDIA\" old-filedia)");
        lisp.AppendLine("  (setvar \"CMDECHO\" old-cmdecho)");
        lisp.AppendLine("  (vl-cmdf \"_.QUIT\" \"Y\")");
        lisp.AppendLine(")");
        return lisp.ToString();
    }

    private static string? FindFullAutoCadFromCoreConsole(string accoreConsolePath)
    {
        string? folder = Path.GetDirectoryName(accoreConsolePath);
        if (string.IsNullOrWhiteSpace(folder))
        {
            return null;
        }

        string acadPath = Path.Combine(folder, "acad.exe");
        return File.Exists(acadPath) ? acadPath : null;
    }

    private static string BuildExportLayoutCommandLisp(string outputDwgPath)
    {
        StringBuilder lisp = new();
        lisp.AppendLine("(vl-load-com)");
        lisp.AppendLine("(defun gopcad-export-error (message)");
        lisp.AppendLine("  (princ (strcat \"\\nGOPCAD_EXPORT_ERROR|\" message))");
        lisp.AppendLine(")");
        lisp.AppendLine("(defun gopcad-first-layout-name (/ dict pair found)");
        lisp.AppendLine("  (setq dict (dictsearch (namedobjdict) \"ACAD_LAYOUT\"))");
        lisp.AppendLine("  (setq found nil)");
        lisp.AppendLine("  (foreach pair dict");
        lisp.AppendLine("    (if (and (= (car pair) 3) (/= (strcase (cdr pair)) \"MODEL\") (not found)) (setq found (cdr pair)))");
        lisp.AppendLine("  )");
        lisp.AppendLine("  found");
        lisp.AppendLine(")");
        lisp.AppendLine("(defun gopcad-export-layout (/ old-filedia old-cmdecho old-cmddia lname result)");
        lisp.AppendLine("  (setq old-filedia (getvar \"FILEDIA\"))");
        lisp.AppendLine("  (setq old-cmdecho (getvar \"CMDECHO\"))");
        lisp.AppendLine("  (setq old-cmddia (getvar \"CMDDIA\"))");
        lisp.AppendLine("  (setvar \"FILEDIA\" 0)");
        lisp.AppendLine("  (setvar \"CMDECHO\" 0)");
        lisp.AppendLine("  (setvar \"CMDDIA\" 0)");
        lisp.AppendLine("  (setq lname (gopcad-first-layout-name))");
        lisp.AppendLine("  (if lname");
        lisp.AppendLine("    (progn");
        lisp.AppendLine("      (vl-cmdf \"_.LAYOUT\" \"_Set\" lname)");
        lisp.AppendLine("      (setq result (vl-catch-all-apply 'vl-cmdf (list \"_.EXPORTLAYOUT\" " + ToLispString(outputDwgPath) + ")))");
        lisp.AppendLine("      (if (vl-catch-all-error-p result)");
        lisp.AppendLine("        (gopcad-export-error (vl-catch-all-error-message result))");
        lisp.AppendLine("        (if (findfile " + ToLispString(outputDwgPath) + ")");
        lisp.AppendLine("          (princ (strcat \"\\nGOPCAD_EXPORT_OK|\" lname))");
        lisp.AppendLine("          (gopcad-export-error \"EXPORTLAYOUT ket thuc nhung khong tao file DWG\")");
        lisp.AppendLine("        )");
        lisp.AppendLine("      )");
        lisp.AppendLine("    )");
        lisp.AppendLine("    (gopcad-export-error \"Khong tim thay layout\")");
        lisp.AppendLine("  )");
        lisp.AppendLine("  (setvar \"FILEDIA\" old-filedia)");
        lisp.AppendLine("  (setvar \"CMDECHO\" old-cmdecho)");
        lisp.AppendLine("  (setvar \"CMDDIA\" old-cmddia)");
        lisp.AppendLine("  (vl-cmdf \"_.QUIT\" \"Y\")");
        lisp.AppendLine(")");
        return lisp.ToString();
    }

    private static string BuildExportLayoutLisp(string outputDwgPath)
    {
        StringBuilder lisp = new();
        lisp.AppendLine("(vl-load-com)");
        lisp.AppendLine("(defun gopcad-export-error (message)");
        lisp.AppendLine("  (princ (strcat \"\\nGOPCAD_EXPORT_ERROR|\" message))");
        lisp.AppendLine(")");
        lisp.AppendLine("(defun gopcad-first-layout-name (/ dict pair found)");
        lisp.AppendLine("  (setq dict (dictsearch (namedobjdict) \"ACAD_LAYOUT\"))");
        lisp.AppendLine("  (setq found nil)");
        lisp.AppendLine("  (foreach pair dict");
        lisp.AppendLine("    (if (and (= (car pair) 3) (/= (strcase (cdr pair)) \"MODEL\") (not found)) (setq found (cdr pair)))");
        lisp.AppendLine("  )");
        lisp.AppendLine("  found");
        lisp.AppendLine(")");
        lisp.AppendLine("(defun gopcad-layout-selection (lname / ss out idx ent etype)");
        lisp.AppendLine("  (setq out (ssadd))");
        lisp.AppendLine("  (setq ss (ssget \"_X\" (list (cons 410 lname))))");
        lisp.AppendLine("  (if ss");
        lisp.AppendLine("    (progn");
        lisp.AppendLine("      (setq idx 0)");
        lisp.AppendLine("      (repeat (sslength ss)");
        lisp.AppendLine("        (setq ent (ssname ss idx))");
        lisp.AppendLine("        (setq etype (cdr (assoc 0 (entget ent))))");
        lisp.AppendLine("        (if (/= etype \"VIEWPORT\") (ssadd ent out))");
        lisp.AppendLine("        (setq idx (1+ idx))");
        lisp.AppendLine("      )");
        lisp.AppendLine("    )");
        lisp.AppendLine("  )");
        lisp.AppendLine("  out");
        lisp.AppendLine(")");
        lisp.AppendLine("(defun gopcad-export-layout (/ old-filedia old-cmdecho old-cmddia old-tilemode lname ss result)");
        lisp.AppendLine("  (setq old-filedia (getvar \"FILEDIA\"))");
        lisp.AppendLine("  (setq old-cmdecho (getvar \"CMDECHO\"))");
        lisp.AppendLine("  (setq old-cmddia (getvar \"CMDDIA\"))");
        lisp.AppendLine("  (setq old-tilemode (getvar \"TILEMODE\"))");
        lisp.AppendLine("  (setvar \"FILEDIA\" 0)");
        lisp.AppendLine("  (setvar \"CMDECHO\" 0)");
        lisp.AppendLine("  (setvar \"CMDDIA\" 0)");
        lisp.AppendLine("  (setq lname (gopcad-first-layout-name))");
        lisp.AppendLine("  (if lname");
        lisp.AppendLine("    (progn");
        lisp.AppendLine("      (vl-cmdf \"_.LAYOUT\" \"_Set\" lname)");
        lisp.AppendLine("      (setq ss (gopcad-layout-selection lname))");
        lisp.AppendLine("      (if (> (sslength ss) 0)");
        lisp.AppendLine("        (progn");
        lisp.AppendLine("          (setq result (vl-catch-all-apply 'vl-cmdf (list \"_.-WBLOCK\" " + ToLispString(outputDwgPath) + " \"\" \"0,0,0\" ss \"\")))");
        lisp.AppendLine("          (if (not (vl-catch-all-error-p result)) (princ (strcat \"\\nGOPCAD_EXPORT_WBLOCK|\" (itoa (sslength ss)) \"|\" lname)))");
        lisp.AppendLine("        )");
        lisp.AppendLine("        (progn");
        lisp.AppendLine("          (princ (strcat \"\\nGOPCAD_EXPORT_NOTE|Layout khong co doi tuong paper space, luu model space: \" lname))");
        lisp.AppendLine("          (setvar \"TILEMODE\" 1)");
        lisp.AppendLine("          (setq result (vl-catch-all-apply 'vl-cmdf (list \"_.SAVEAS\" \"2018\" " + ToLispString(outputDwgPath) + ")))");
        lisp.AppendLine("        )");
        lisp.AppendLine("      )");
        lisp.AppendLine("      (if (vl-catch-all-error-p result)");
        lisp.AppendLine("        (gopcad-export-error (vl-catch-all-error-message result))");
        lisp.AppendLine("        (if (findfile " + ToLispString(outputDwgPath) + ")");
        lisp.AppendLine("          (princ (strcat \"\\nGOPCAD_EXPORT_OK|\" lname))");
        lisp.AppendLine("          (gopcad-export-error \"SAVEAS ket thuc nhung khong tao file DWG\")");
        lisp.AppendLine("        )");
        lisp.AppendLine("      )");
        lisp.AppendLine("    )");
        lisp.AppendLine("    (gopcad-export-error \"Khong tim thay layout\")");
        lisp.AppendLine("  )");
        lisp.AppendLine("  (setvar \"TILEMODE\" old-tilemode)");
        lisp.AppendLine("  (setvar \"FILEDIA\" old-filedia)");
        lisp.AppendLine("  (setvar \"CMDECHO\" old-cmdecho)");
        lisp.AppendLine("  (setvar \"CMDDIA\" old-cmddia)");
        lisp.AppendLine("  (vl-cmdf \"_.QUIT\" \"Y\")");
        lisp.AppendLine(")");
        return lisp.ToString();
    }

    private static string BuildBlankDxf()
    {
        return """
0
SECTION
2
HEADER
0
ENDSEC
0
SECTION
2
TABLES
0
ENDSEC
0
SECTION
2
ENTITIES
0
ENDSEC
0
EOF
""";
    }

    private static string BuildAutoCadLispSafe(List<string> copiedDwgs, IReadOnlyList<string> displayNames, string masterDwgPath, CadMergeMode mergeMode)
    {
        StringBuilder lisp = new();
        lisp.AppendLine("(vl-load-com)");
        lisp.AppendLine("(defun gopcad-progress (current total stage filename) (princ (strcat \"\\nGOPCAD_PROGRESS|\" (itoa current) \"|\" (itoa total) \"|\" stage \"|\" filename)))");
        lisp.AppendLine("(defun gopcad-warn (label message) (princ (strcat \"\\nGOPCAD_WARN|\" label \"|\" message)))");
        lisp.AppendLine("(defun gopcad-try (fn args label / result)");
        lisp.AppendLine("  (setq result (vl-catch-all-apply fn args))");
        lisp.AppendLine("  (if (vl-catch-all-error-p result) (gopcad-warn label (vl-catch-all-error-message result)))");
        lisp.AppendLine("  result");
        lisp.AppendLine(")");
        lisp.AppendLine("(defun gopcad-pt (x y) (strcat (rtos x 2 8) \",\" (rtos y 2 8) \",0\"))");
        lisp.AppendLine("(defun gopcad-safe-layout-name (idx) (strcat \"Sheet_\" (if (< idx 10) \"00\" (if (< idx 100) \"0\" \"\")) (itoa idx)))");
        lisp.AppendLine("(defun gopcad-layout-exists (lname / dict pair found)");
        lisp.AppendLine("  (setq dict (dictsearch (namedobjdict) \"ACAD_LAYOUT\"))");
        lisp.AppendLine("  (setq found nil)");
        lisp.AppendLine("  (foreach pair dict (if (and (= (car pair) 3) (= (strcase (cdr pair)) (strcase lname))) (setq found T)))");
        lisp.AppendLine("  found");
        lisp.AppendLine(")");
        lisp.AppendLine("(defun gopcad-set-layout (lname / result)");
        lisp.AppendLine("  (if (gopcad-layout-exists lname)");
        lisp.AppendLine("    (progn (setq result (vl-catch-all-apply 'vl-cmdf (list \"_.LAYOUT\" \"_Set\" lname))) (not (vl-catch-all-error-p result)))");
        lisp.AppendLine("    nil)");
        lisp.AppendLine(")");
        lisp.AppendLine("(defun gopcad-new-layout (lname / result)");
        lisp.AppendLine("  (setq result (vl-catch-all-apply 'vl-cmdf (list \"_.LAYOUT\" \"_New\" lname)))");
        lisp.AppendLine("  (if (vl-catch-all-error-p result) nil T)");
        lisp.AppendLine(")");
        lisp.AppendLine("(defun gopcad-delete-layout (lname / result)");
        lisp.AppendLine("  (if (gopcad-layout-exists lname) (vl-catch-all-apply 'vl-cmdf (list \"_.LAYOUT\" \"_Delete\" lname)))");
        lisp.AppendLine(")");
        lisp.AppendLine("(defun gopcad-delete-viewports (lname / ss i ent)");
        lisp.AppendLine("  (setq ss (ssget \"X\" (list '(0 . \"VIEWPORT\") (cons 410 lname))))");
        lisp.AppendLine("  (if ss (progn (setq i 0) (repeat (sslength ss) (setq ent (ssname ss i)) (entdel ent) (setq i (1+ i)))))");
        lisp.AppendLine(")");
        lisp.AppendLine("(defun gopcad-clear-layout (lname / ss i ent)");
        lisp.AppendLine("  (if (gopcad-layout-exists lname)");
        lisp.AppendLine("    (progn (setq ss (ssget \"X\" (list (cons 410 lname)))) (if ss (progn (setq i 0) (repeat (sslength ss) (setq ent (ssname ss i)) (entdel ent) (setq i (1+ i)))))))");
        lisp.AppendLine(")");
        lisp.AppendLine("(defun gopcad-vpt (value) (vlax-safearray->list (vlax-variant-value value)))");
        lisp.AppendLine("(defun gopcad-ensure-layer (lname / result)");
        lisp.AppendLine("  (if (not (tblsearch \"LAYER\" lname)) (vl-catch-all-apply 'vl-cmdf (list \"_.-LAYER\" \"_Make\" lname \"\")))");
        lisp.AppendLine(")");
        lisp.AppendLine("(defun gopcad-create-paper-sheet (lname paperW paperH / solid border)");
        lisp.AppendLine("  (gopcad-ensure-layer \"GOPCAD_PAPER\")");
        lisp.AppendLine("  (setq solid (entmakex (list");
        lisp.AppendLine("    '(0 . \"SOLID\")");
        lisp.AppendLine("    '(100 . \"AcDbEntity\")");
        lisp.AppendLine("    '(8 . \"GOPCAD_PAPER\")");
        lisp.AppendLine("    '(62 . 7)");
        lisp.AppendLine("    '(420 . 16777215)");
        lisp.AppendLine("    (cons 410 lname)");
        lisp.AppendLine("    '(100 . \"AcDbTrace\")");
        lisp.AppendLine("    '(10 0.0 0.0 0.0)");
        lisp.AppendLine("    (cons 11 (list paperW 0.0 0.0))");
        lisp.AppendLine("    (cons 12 (list 0.0 paperH 0.0))");
        lisp.AppendLine("    (cons 13 (list paperW paperH 0.0))");
        lisp.AppendLine("  )))");
        lisp.AppendLine("  (setq border (entmakex (list");
        lisp.AppendLine("    '(0 . \"LWPOLYLINE\")");
        lisp.AppendLine("    '(100 . \"AcDbEntity\")");
        lisp.AppendLine("    '(8 . \"GOPCAD_PAPER\")");
        lisp.AppendLine("    '(62 . 7)");
        lisp.AppendLine("    (cons 410 lname)");
        lisp.AppendLine("    '(100 . \"AcDbPolyline\")");
        lisp.AppendLine("    '(90 . 4)");
        lisp.AppendLine("    '(70 . 1)");
        lisp.AppendLine("    '(43 . 0.0)");
        lisp.AppendLine("    '(10 0.0 0.0)");
        lisp.AppendLine("    (cons 10 (list paperW 0.0))");
        lisp.AppendLine("    (cons 10 (list paperW paperH))");
        lisp.AppendLine("    (cons 10 (list 0.0 paperH))");
        lisp.AppendLine("  )))");
        lisp.AppendLine("  (if solid (vl-catch-all-apply 'vl-cmdf (list \"_.DRAWORDER\" solid \"\" \"_Back\")))");
        lisp.AppendLine("  (list solid border)");
        lisp.AppendLine(")");
        lisp.AppendLine("(defun gopcad-insert-dwg (nameOrPath x y / before after result)");
        lisp.AppendLine("  (setq before (entlast))");
        lisp.AppendLine("  (setq result (vl-catch-all-apply 'vl-cmdf (list \"_.-INSERT\" nameOrPath (gopcad-pt x y) \"1\" \"1\" \"0\")))");
        lisp.AppendLine("  (setq after (entlast))");
        lisp.AppendLine("  (if (and (not (vl-catch-all-error-p result)) after (/= before after)) after nil)");
        lisp.AppendLine(")");
        lisp.AppendLine("(defun gopcad-set-pair (ed code value / old)");
        lisp.AppendLine("  (setq old (assoc code ed))");
        lisp.AppendLine("  (if old (subst (cons code value) old ed) (append ed (list (cons code value))))");
        lisp.AppendLine(")");
        lisp.AppendLine("(defun gopcad-layout-object (lname / acad doc layouts layout)");
        lisp.AppendLine("  (setq acad (vl-catch-all-apply 'vlax-get-acad-object nil))");
        lisp.AppendLine("  (if (vl-catch-all-error-p acad)");
        lisp.AppendLine("    nil");
        lisp.AppendLine("    (progn");
        lisp.AppendLine("      (setq doc (vl-catch-all-apply 'vla-get-ActiveDocument (list acad)))");
        lisp.AppendLine("      (if (vl-catch-all-error-p doc)");
        lisp.AppendLine("        nil");
        lisp.AppendLine("        (progn");
        lisp.AppendLine("          (setq layouts (vl-catch-all-apply 'vla-get-Layouts (list doc)))");
        lisp.AppendLine("          (if (vl-catch-all-error-p layouts)");
        lisp.AppendLine("            nil");
        lisp.AppendLine("            (progn");
        lisp.AppendLine("              (setq layout (vl-catch-all-apply 'vla-Item (list layouts lname)))");
        lisp.AppendLine("              (if (vl-catch-all-error-p layout) nil layout)");
        lisp.AppendLine("            )");
        lisp.AppendLine("          )");
        lisp.AppendLine("        )");
        lisp.AppendLine("      )");
        lisp.AppendLine("    )");
        lisp.AppendLine("  )");
        lisp.AppendLine(")");
        lisp.AppendLine("(defun gopcad-pick-a1-media (layout / names media chosen upper)");
        lisp.AppendLine("  (setq chosen \"ISO_full_bleed_A1_(841.00_x_594.00_MM)\")");
        lisp.AppendLine("  (setq names (vl-catch-all-apply 'vla-GetCanonicalMediaNames (list layout)))");
        lisp.AppendLine("  (if (not (vl-catch-all-error-p names))");
        lisp.AppendLine("    (progn");
        lisp.AppendLine("      (foreach media (vlax-safearray->list (vlax-variant-value names))");
        lisp.AppendLine("        (setq upper (strcase media))");
        lisp.AppendLine("        (if (and (wcmatch upper \"*A1*\") (wcmatch upper \"*841*\") (wcmatch upper \"*594*\")) (setq chosen media))");
        lisp.AppendLine("      )");
        lisp.AppendLine("      (foreach media (vlax-safearray->list (vlax-variant-value names))");
        lisp.AppendLine("        (setq upper (strcase media))");
        lisp.AppendLine("        (if (and (wcmatch upper \"*841*\") (wcmatch upper \"*594*\")) (setq chosen media))");
        lisp.AppendLine("      )");
        lisp.AppendLine("    )");
        lisp.AppendLine("  )");
        lisp.AppendLine("  chosen");
        lisp.AppendLine(")");
        lisp.AppendLine("(defun gopcad-configure-a1-layout (lname / dict entry layoutEnt ed flags layout media)");
        lisp.AppendLine("  (setq dict (dictsearch (namedobjdict) \"ACAD_LAYOUT\"))");
        lisp.AppendLine("  (setq entry (dictsearch (cdr (assoc -1 dict)) lname))");
        lisp.AppendLine("  (setq layoutEnt (if entry (cdr (assoc -1 entry)) nil))");
        lisp.AppendLine("  (setvar \"LIMMIN\" '(0.0 0.0))");
        lisp.AppendLine("  (setvar \"LIMMAX\" '(841.0 594.0))");
        lisp.AppendLine("  (if layoutEnt");
        lisp.AppendLine("    (progn");
        lisp.AppendLine("      (setq ed (entget layoutEnt))");
        lisp.AppendLine("      (setq ed (gopcad-set-pair ed 2 \"DWG To PDF.pc3\"))");
        lisp.AppendLine("      (setq ed (gopcad-set-pair ed 4 \"ISO_full_bleed_A1_(841.00_x_594.00_MM)\"))");
        lisp.AppendLine("      (setq ed (gopcad-set-pair ed 40 0.0))");
        lisp.AppendLine("      (setq ed (gopcad-set-pair ed 41 0.0))");
        lisp.AppendLine("      (setq ed (gopcad-set-pair ed 42 0.0))");
        lisp.AppendLine("      (setq ed (gopcad-set-pair ed 43 0.0))");
        lisp.AppendLine("      (setq ed (gopcad-set-pair ed 44 841.0))");
        lisp.AppendLine("      (setq ed (gopcad-set-pair ed 45 594.0))");
        lisp.AppendLine("      (setq ed (gopcad-set-pair ed 46 0.0))");
        lisp.AppendLine("      (setq ed (gopcad-set-pair ed 47 0.0))");
        lisp.AppendLine("      (setq ed (gopcad-set-pair ed 72 1))");
        lisp.AppendLine("      (setq ed (gopcad-set-pair ed 73 0))");
        lisp.AppendLine("      (setq ed (gopcad-set-pair ed 74 5))");
        lisp.AppendLine("      (setq flags (cdr (assoc 70 ed)))");
        lisp.AppendLine("      (if flags (setq ed (subst (cons 70 (logior flags 2048 4096)) (assoc 70 ed) ed)))");
        lisp.AppendLine("      (entmod ed)");
        lisp.AppendLine("      (entupd layoutEnt)");
        lisp.AppendLine("    )");
        lisp.AppendLine("  )");
        lisp.AppendLine("  (setq layout (gopcad-layout-object lname))");
        lisp.AppendLine("  (if layout");
        lisp.AppendLine("    (progn");
        lisp.AppendLine("      (vl-catch-all-apply 'vla-RefreshPlotDeviceInfo (list layout))");
        lisp.AppendLine("      (vl-catch-all-apply 'vla-put-ConfigName (list layout \"DWG To PDF.pc3\"))");
        lisp.AppendLine("      (vl-catch-all-apply 'vla-RefreshPlotDeviceInfo (list layout))");
        lisp.AppendLine("      (setq media (gopcad-pick-a1-media layout))");
        lisp.AppendLine("      (gopcad-warn (strcat \"PAPER_MEDIA|\" lname) media)");
        lisp.AppendLine("      (vl-catch-all-apply 'vla-put-CanonicalMediaName (list layout media))");
        lisp.AppendLine("      (vl-catch-all-apply 'vla-put-PaperUnits (list layout 1))");
        lisp.AppendLine("      (vl-catch-all-apply 'vla-put-PlotRotation (list layout 1))");
        lisp.AppendLine("      (vl-catch-all-apply 'vla-put-PlotType (list layout 5))");
        lisp.AppendLine("      (vl-catch-all-apply 'vla-put-CenterPlot (list layout :vlax-true))");
        lisp.AppendLine("      (vl-catch-all-apply 'vla-put-UseStandardScale (list layout :vlax-true))");
        lisp.AppendLine("      (vl-catch-all-apply 'vla-put-StandardScale (list layout 0))");
        lisp.AppendLine("      (vl-catch-all-apply 'vla-RefreshPlotDeviceInfo (list layout))");
        lisp.AppendLine("    )");
        lisp.AppendLine("    (gopcad-warn (strcat \"LAYOUT_OBJECT|\" lname) \"Khong lay duoc layout object\")");
        lisp.AppendLine("  )");
        lisp.AppendLine("  layoutEnt");
        lisp.AppendLine(")");
        lisp.AppendLine("(defun gopcad-merge-extents (a b / minA maxA minB maxB)");
        lisp.AppendLine("  (cond");
        lisp.AppendLine("    ((not a) b)");
        lisp.AppendLine("    ((not b) a)");
        lisp.AppendLine("    (T");
        lisp.AppendLine("      (setq minA (car a) maxA (cadr a) minB (car b) maxB (cadr b))");
        lisp.AppendLine("      (list");
        lisp.AppendLine("        (list (min (car minA) (car minB)) (min (cadr minA) (cadr minB)) 0.0)");
        lisp.AppendLine("        (list (max (car maxA) (car maxB)) (max (cadr maxA) (cadr maxB)) 0.0)");
        lisp.AppendLine("      )");
        lisp.AppendLine("    )");
        lisp.AppendLine("  )");
        lisp.AppendLine(")");
        lisp.AppendLine("(defun gopcad-point-extents (pt dx dy sx sy / x y)");
        lisp.AppendLine("  (setq x (+ dx (* sx (car pt))))");
        lisp.AppendLine("  (setq y (+ dy (* sy (cadr pt))))");
        lisp.AppendLine("  (list (list x y 0.0) (list x y 0.0))");
        lisp.AppendLine(")");
        lisp.AppendLine("(defun gopcad-circle-extents (center radius dx dy sx sy / x y rx ry)");
        lisp.AppendLine("  (setq x (+ dx (* sx (car center))))");
        lisp.AppendLine("  (setq y (+ dy (* sy (cadr center))))");
        lisp.AppendLine("  (setq rx (abs (* sx radius)) ry (abs (* sy radius)))");
        lisp.AppendLine("  (list (list (- x rx) (- y ry) 0.0) (list (+ x rx) (+ y ry) 0.0))");
        lisp.AppendLine(")");
        lisp.AppendLine("(defun gopcad-extent-entity-type-p (type)");
        lisp.AppendLine("  (member type '(\"LINE\" \"LWPOLYLINE\" \"POLYLINE\" \"VERTEX\" \"CIRCLE\" \"ARC\" \"ELLIPSE\" \"SOLID\" \"TRACE\" \"HATCH\" \"SPLINE\"))");
        lisp.AppendLine(")");
        lisp.AppendLine("(defun gopcad-block-extents (blockName dx dy sx sy depth / blockEnt cursor data type pair code point ext child ins csx csy radius center)");
        lisp.AppendLine("  (if (> depth 8)");
        lisp.AppendLine("    nil");
        lisp.AppendLine("    (progn");
        lisp.AppendLine("      (setq blockEnt (if blockName (tblobjname \"BLOCK\" blockName) nil))");
        lisp.AppendLine("      (setq cursor (if blockEnt (entnext blockEnt) nil) ext nil)");
        lisp.AppendLine("  (while cursor");
        lisp.AppendLine("    (setq data (entget cursor))");
        lisp.AppendLine("    (if (= (cdr (assoc 0 data)) \"ENDBLK\")");
        lisp.AppendLine("      (setq cursor nil)");
        lisp.AppendLine("      (progn");
        lisp.AppendLine("        (setq type (cdr (assoc 0 data)))");
        lisp.AppendLine("        (if (= type \"INSERT\")");
        lisp.AppendLine("          (progn");
        lisp.AppendLine("            (setq ins (if (assoc 10 data) (cdr (assoc 10 data)) '(0.0 0.0 0.0)))");
        lisp.AppendLine("            (setq csx (if (assoc 41 data) (cdr (assoc 41 data)) 1.0))");
        lisp.AppendLine("            (setq csy (if (assoc 42 data) (cdr (assoc 42 data)) 1.0))");
        lisp.AppendLine("            (setq child (gopcad-block-extents (cdr (assoc 2 data)) (+ dx (* sx (car ins))) (+ dy (* sy (cadr ins))) (* sx csx) (* sy csy) (1+ depth)))");
        lisp.AppendLine("            (setq ext (gopcad-merge-extents ext child))");
        lisp.AppendLine("          )");
        lisp.AppendLine("        )");
        lisp.AppendLine("        (if (and (member type '(\"CIRCLE\" \"ARC\")) (assoc 10 data) (assoc 40 data))");
        lisp.AppendLine("          (setq ext (gopcad-merge-extents ext (gopcad-circle-extents (cdr (assoc 10 data)) (cdr (assoc 40 data)) dx dy sx sy)))");
        lisp.AppendLine("        )");
        lisp.AppendLine("        (if (gopcad-extent-entity-type-p type)");
        lisp.AppendLine("          (foreach pair data");
        lisp.AppendLine("            (setq code (car pair) point (cdr pair))");
        lisp.AppendLine("            (if (and (>= code 10) (<= code 18) (listp point) (numberp (car point)) (numberp (cadr point)))");
        lisp.AppendLine("              (setq ext (gopcad-merge-extents ext (gopcad-point-extents point dx dy sx sy)))");
        lisp.AppendLine("            )");
        lisp.AppendLine("          )");
        lisp.AppendLine("        )");
        lisp.AppendLine("        (setq cursor (entnext cursor))");
        lisp.AppendLine("      )");
        lisp.AppendLine("    )");
        lisp.AppendLine("  )");
        lisp.AppendLine("      ext");
        lisp.AppendLine("    )");
        lisp.AppendLine("  )");
        lisp.AppendLine(")");
        lisp.AppendLine("(defun gopcad-entity-extents (ent / ed blockName insertion sx sy ext)");
        lisp.AppendLine("  (setq ed (entget ent))");
        lisp.AppendLine("  (setq blockName (cdr (assoc 2 ed)))");
        lisp.AppendLine("  (setq insertion (if (assoc 10 ed) (cdr (assoc 10 ed)) '(0.0 0.0 0.0)))");
        lisp.AppendLine("  (setq sx (if (assoc 41 ed) (cdr (assoc 41 ed)) 1.0))");
        lisp.AppendLine("  (setq sy (if (assoc 42 ed) (cdr (assoc 42 ed)) 1.0))");
        lisp.AppendLine("  (setq ext (gopcad-block-extents blockName (car insertion) (cadr insertion) sx sy 0))");
        lisp.AppendLine("  (if ext ext (list '(0.0 0.0 0.0) '(841.0 594.0 0.0)))");
        lisp.AppendLine(")");
        lisp.AppendLine("(defun gopcad-paper-size (lname / layout w h p1 lim)");
        lisp.AppendLine("  (setq w 841.0 h 594.0)");
        lisp.AppendLine("  (setq layout (gopcad-layout-object lname))");
        lisp.AppendLine("  (if layout");
        lisp.AppendLine("    (progn");
        lisp.AppendLine("      (vl-catch-all-apply 'vla-RefreshPlotDeviceInfo (list layout))");
        lisp.AppendLine("      (setq p1 (vl-catch-all-apply 'vla-GetPaperSize (list layout 'w 'h)))");
        lisp.AppendLine("      (if (not (vl-catch-all-error-p p1))");
        lisp.AppendLine("        (progn");
        lisp.AppendLine("          (if (and (numberp w) (> w 0.0)) (setq w w))");
        lisp.AppendLine("          (if (and (numberp h) (> h 0.0)) (setq h h))");
        lisp.AppendLine("        )");
        lisp.AppendLine("      )");
        lisp.AppendLine("    )");
        lisp.AppendLine("  )");
        lisp.AppendLine("  (if (< w h) (progn (setq lim w) (setq w h) (setq h lim)))");
        lisp.AppendLine("  (if (< w 100.0) (setq w (* w 25.4)))");
        lisp.AppendLine("  (if (< h 100.0) (setq h (* h 25.4)))");
        lisp.AppendLine("  (if (or (< w 100.0) (< h 100.0) (> w 2000.0) (> h 2000.0)) (setq w 841.0 h 594.0))");
        lisp.AppendLine("  (gopcad-warn (strcat \"PAPER_SIZE|\" lname) (strcat (rtos w 2 2) \"x\" (rtos h 2 2)))");
        lisp.AppendLine("  (list w h)");
        lisp.AppendLine(")");
        lisp.AppendLine("(defun gopcad-fit-layout-entity (ent lname / ext minp maxp width height margin dx dy paperW paperH)");
        lisp.AppendLine("  (setq ext (gopcad-entity-extents ent))");
        lisp.AppendLine("  (if (and ext (= (length ext) 2))");
        lisp.AppendLine("    (progn");
        lisp.AppendLine("      (setq minp (car ext) maxp (cadr ext))");
        lisp.AppendLine("      (setq width (abs (- (car maxp) (car minp))) height (abs (- (cadr maxp) (cadr minp))))");
        lisp.AppendLine("      (if (and (> width 0.000001) (> height 0.000001))");
        lisp.AppendLine("        (progn");
        lisp.AppendLine("          (setq margin (max 3.0 (* (min width height) 0.01)))");
        lisp.AppendLine("          (setq dx (- margin (car minp)) dy (- margin (cadr minp)))");
        lisp.AppendLine("          (vl-catch-all-apply 'vl-cmdf (list \"_.MOVE\" ent \"\" \"0,0,0\" (gopcad-pt dx dy)))");
        lisp.AppendLine("          (setq paperW (+ width (* 2.0 margin)) paperH (+ height (* 2.0 margin)))");
        lisp.AppendLine("          (gopcad-create-paper-sheet lname paperW paperH)");
        lisp.AppendLine("          (gopcad-warn (strcat \"LAYOUT_BOX|\" lname) (strcat (rtos paperW 2 2) \"x\" (rtos paperH 2 2) \"; scale=1\"))");
        lisp.AppendLine("        )");
        lisp.AppendLine("      )");
        lisp.AppendLine("    )");
        lisp.AppendLine("  )");
        lisp.AppendLine("  ent");
        lisp.AppendLine(")");
        lisp.AppendLine("(defun gopcad-place-model-entity (ent base-x) (+ base-x 900.0))");
        lisp.AppendLine("(defun gopcad-main (/ files total idx item path blockName ent ed items lname layerName base-x result layoutEnt)");
        lisp.AppendLine("  (setvar \"FILEDIA\" 0)");
        lisp.AppendLine("  (setvar \"CMDECHO\" 0)");
        lisp.AppendLine("  (setvar \"INSUNITS\" 0)");
        lisp.AppendLine("  (setvar \"TILEMODE\" 1)");
        lisp.AppendLine("  (setvar \"XREFNOTIFY\" 0)");
        lisp.AppendLine("  (gopcad-progress 0 1 \"Da vao script\" \"gopcad-main\")");
        lisp.AppendLine("  (setq files (list");
        for (int index = 0; index < copiedDwgs.Count; index++)
        {
            string dwg = copiedDwgs[index];
            string blockName = Path.GetFileNameWithoutExtension(dwg);
            // Phần tử thứ 3 chỉ để in lên thanh tiến trình: tên file GỐC (bỏ dấu), không phải CAD_0001.dwg.
            lisp.AppendLine($"    (list {ToLispString(dwg)} {ToLispString(blockName)} {ToLispString(displayNames[index])})");
        }
        lisp.AppendLine("  ))");
        lisp.AppendLine("  (setq total (length files) idx 0 items nil base-x 0.0)");
        lisp.AppendLine("  (foreach item files");
        lisp.AppendLine("    (setq idx (1+ idx) path (car item) blockName (cadr item))");
        lisp.AppendLine("    (setq layerName (strcat \"GOPCAD_\" (if (< idx 10) \"00\" (if (< idx 100) \"0\" \"\")) (itoa idx)))");
        lisp.AppendLine("    (gopcad-ensure-layer layerName)");
        lisp.AppendLine("    (gopcad-progress idx total \"Dang chen model\" (caddr item))");
        lisp.AppendLine(mergeMode == CadMergeMode.StandaloneDwg
            ? "    (setq ent (gopcad-insert-dwg path base-x 0.0))"
            : "    (progn (vl-catch-all-apply 'vl-cmdf (list \"_.-XREF\" \"ATTACH\" path \"0,0,0\" \"1\" \"1\" \"0\")) (setq ent (entlast)))");
        lisp.AppendLine("    (if ent");
        lisp.AppendLine("      (progn");
        lisp.AppendLine("        (setq ed (entget ent))");
        lisp.AppendLine("        (if (assoc 8 ed) (entmod (subst (cons 8 layerName) (assoc 8 ed) ed)) (entmod (append ed (list (cons 8 layerName)))))");
        lisp.AppendLine("        (entupd ent)");
        lisp.AppendLine("        (setq base-x (gopcad-place-model-entity ent base-x))");
        lisp.AppendLine("        (setq items (append items (list (list idx layerName path blockName))))");
        lisp.AppendLine("      )");
        lisp.AppendLine("      (gopcad-progress idx total \"Khong chen duoc model\" (caddr item)))");
        lisp.AppendLine("  )");
        lisp.AppendLine("  (foreach item items");
        lisp.AppendLine("    (setq idx (car item) layerName (cadr item) path (caddr item) blockName (cadddr item))");
        lisp.AppendLine("    (setq lname (gopcad-safe-layout-name idx))");
        lisp.AppendLine("    (gopcad-progress idx total \"Dang tao layout\" lname)");
        lisp.AppendLine("    (gopcad-delete-layout lname)");
        lisp.AppendLine("    (if (gopcad-new-layout lname)");
        lisp.AppendLine("      (progn");
        lisp.AppendLine("        (gopcad-set-layout lname)");
        lisp.AppendLine("        (gopcad-try 'gopcad-delete-viewports (list lname) (strcat \"DELETE_VIEWPORT|\" lname))");
        lisp.AppendLine("        (gopcad-try 'gopcad-configure-a1-layout (list lname) (strcat \"CONFIG_LAYOUT|\" lname))");
        lisp.AppendLine("        (setq layoutEnt (gopcad-insert-dwg blockName 0.0 0.0))");
        lisp.AppendLine("        (if layoutEnt (progn (gopcad-try 'gopcad-fit-layout-entity (list layoutEnt lname) (strcat \"FIT_LAYOUT|\" lname)) (gopcad-progress idx total \"Da tao layout\" lname)) (gopcad-progress idx total \"Bo qua insert layout\" lname))");
        lisp.AppendLine("      )");
        lisp.AppendLine("      (gopcad-progress idx total \"Bo qua layout\" lname))");
        lisp.AppendLine("  )");
        lisp.AppendLine("  (if (> (length items) 0) (gopcad-set-layout \"Sheet_001\"))");
        lisp.AppendLine("  (if (gopcad-layout-exists \"Layout1\") (if (not (gopcad-delete-layout \"Layout1\")) (gopcad-clear-layout \"Layout1\")))");
        lisp.AppendLine("  (if (gopcad-layout-exists \"Layout2\") (if (not (gopcad-delete-layout \"Layout2\")) (gopcad-clear-layout \"Layout2\")))");
        lisp.AppendLine("  (if (> (length items) 0) (gopcad-set-layout \"Sheet_001\"))");
        lisp.AppendLine("  (setvar \"TILEMODE\" 1)");
        lisp.AppendLine("  (gopcad-progress total total \"Dang luu master\" " + ToLispString(Path.GetFileName(masterDwgPath)) + ")");
        lisp.AppendLine("  (if (findfile " + ToLispString(masterDwgPath) + ") (vl-file-delete " + ToLispString(masterDwgPath) + "))");
        lisp.AppendLine("  (setq result (vl-catch-all-apply 'vl-cmdf (list \"_.SAVEAS\" \"2018\" " + ToLispString(masterDwgPath) + ")))");
        lisp.AppendLine("  (if (vl-catch-all-error-p result) (princ (strcat \"\\nGOPCAD_ERROR|SAVEAS: \" (vl-catch-all-error-message result))))");
        lisp.AppendLine("  (if (findfile " + ToLispString(masterDwgPath) + ") (gopcad-progress total total \"Da luu master\" " + ToLispString(Path.GetFileName(masterDwgPath)) + "))");
        lisp.AppendLine("  (vl-cmdf \"_.QUIT\" \"Y\")");
        lisp.AppendLine(")");
        return lisp.ToString();
    }

    private static void WriteConsoleLog(string consoleLogPath, StringBuilder output, StringBuilder error)
    {
        StringBuilder log = new();
        log.AppendLine("=== AutoCAD Core Console Output ===");
        log.Append(output);
        log.AppendLine();
        log.AppendLine("=== AutoCAD Core Console Error ===");
        log.Append(error);
        File.WriteAllText(consoleLogPath, log.ToString(), Encoding.UTF8);
    }

    private static string? NormalizeConsoleLine(string? line)
    {
        return line?.Replace("\0", string.Empty, StringComparison.Ordinal).TrimEnd();
    }

    private static bool HasAutoCadFailure(string log)
    {
        return log.Contains("GOPCAD_ERROR|", StringComparison.OrdinalIgnoreCase)
            || log.Contains("GOPCAD_EXPORT_ERROR|", StringComparison.OrdinalIgnoreCase)
            || log.Contains("GOPCAD_CLEANUP_ERROR|", StringComparison.OrdinalIgnoreCase)
            || log.Contains("; error:", StringComparison.OrdinalIgnoreCase)
            || log.Contains("*Invalid*", StringComparison.OrdinalIgnoreCase)
            || log.Contains("Can't find file", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetLogTail(string log)
    {
        string[] lines = log
            .Replace("\0", string.Empty, StringComparison.Ordinal)
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return string.Join(Environment.NewLine, lines.TakeLast(24));
    }

    private static void TryKillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }
    }

    private static void HideAutoCadWindow(Process process, bool shouldHide)
    {
        if (!shouldHide)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            for (int attempt = 0; attempt < 30; attempt++)
            {
                try
                {
                    if (process.HasExited)
                    {
                        return;
                    }

                    process.Refresh();
                    IntPtr handle = process.MainWindowHandle;
                    if (handle != IntPtr.Zero)
                    {
                        ShowWindow(handle, SwMinimize);
                        ShowWindow(handle, SwHide);
                        return;
                    }
                }
                catch
                {
                    return;
                }

                await Task.Delay(500);
            }
        });
    }

    private static bool WaitForAutoCadProcess(Process process, string? expectedOutputPath, StringBuilder output, string timeoutTag)
    {
        DateTime deadline = DateTime.Now.Add(AutoCadProcessTimeout);
        DateTime? outputSeenAt = null;
        long lastOutputSize = -1;

        while (!process.WaitForExit(1000))
        {
            if (!string.IsNullOrWhiteSpace(expectedOutputPath) && File.Exists(expectedOutputPath))
            {
                long currentSize = new FileInfo(expectedOutputPath).Length;
                if (currentSize > 0 && currentSize == lastOutputSize)
                {
                    outputSeenAt ??= DateTime.Now;
                    if (DateTime.Now - outputSeenAt.Value >= TimeSpan.FromSeconds(8))
                    {
                        output.AppendLine("GOPCAD_NOTE|Da thay file ket qua, dong AutoCAD dang con mo de tiep tuc.");
                        TryKillProcess(process);
                        process.WaitForExit();
                        return true;
                    }
                }
                else
                {
                    lastOutputSize = currentSize;
                    outputSeenAt = DateTime.Now;
                }
            }

            if (DateTime.Now >= deadline)
            {
                TryKillProcess(process);
                output.AppendLine($"{timeoutTag}|AutoCAD Core Console qua thoi gian cho {AutoCadProcessTimeout.TotalMinutes:0} phut");
                process.WaitForExit();
                return false;
            }
        }

        return false;
    }

    private static async Task<bool> WaitForAutoCadProcessAsync(Process process, string? expectedOutputPath, StringBuilder output, string timeoutTag)
    {
        DateTime deadline = DateTime.Now.Add(AutoCadProcessTimeout);
        DateTime? outputSeenAt = null;
        long lastOutputSize = -1;

        while (!process.HasExited)
        {
            await Task.Delay(1000);

            if (!string.IsNullOrWhiteSpace(expectedOutputPath) && File.Exists(expectedOutputPath))
            {
                long currentSize = new FileInfo(expectedOutputPath).Length;
                if (currentSize > 0 && currentSize == lastOutputSize)
                {
                    outputSeenAt ??= DateTime.Now;
                    if (DateTime.Now - outputSeenAt.Value >= TimeSpan.FromSeconds(8))
                    {
                        output.AppendLine("GOPCAD_NOTE|Da thay file ket qua, dong AutoCAD dang con mo de tiep tuc.");
                        TryKillProcess(process);
                        await process.WaitForExitAsync();
                        return true;
                    }
                }
                else
                {
                    lastOutputSize = currentSize;
                    outputSeenAt = DateTime.Now;
                }
            }

            if (DateTime.Now >= deadline)
            {
                TryKillProcess(process);
                output.AppendLine($"{timeoutTag}|AutoCAD Core Console qua thoi gian cho {AutoCadProcessTimeout.TotalMinutes:0} phut");
                await process.WaitForExitAsync();
                return false;
            }
        }

        return false;
    }

    private static CadProgress? ParseAutoCadProgress(string line)
    {
        int markerIndex = line.IndexOf("GOPCAD_PROGRESS|", StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return null;
        }

        string marker = line[markerIndex..].Trim();
        string[] parts = marker.Split('|');
        if (parts.Length < 5 || !int.TryParse(parts[1], out int current) || !int.TryParse(parts[2], out int total))
        {
            return null;
        }

        return new CadProgress(current, total, parts[3], parts[4]);
    }

    private static string ToLispString(string value)
    {
        return "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    private static string BuildBatch(string scriptPath, string inputDrawingPath)
    {
        return $$"""
@echo off
setlocal
set SCRIPT={{scriptPath}}
set INPUT={{inputDrawingPath}}

for %%A in (
  "C:\Program Files\Autodesk\AutoCAD 2026\accoreconsole.exe"
  "C:\Program Files\Autodesk\AutoCAD 2025\accoreconsole.exe"
  "C:\Program Files\Autodesk\AutoCAD 2024\accoreconsole.exe"
  "C:\Program Files\Autodesk\AutoCAD 2023\accoreconsole.exe"
  "C:\Program Files\Autodesk\AutoCAD 2022\accoreconsole.exe"
  "C:\Program Files\Autodesk\AutoCAD 2021\accoreconsole.exe"
  "C:\Program Files\Autodesk\AutoCAD 2020\accoreconsole.exe"
) do (
  if exist %%~A (
    %%~A /i "%INPUT%" /s "%SCRIPT%"
    goto :done
  )
)

echo Khong tim thay accoreconsole.exe. Hay mo AutoCAD va chay file SCR nay:
echo %SCRIPT%
pause

:done
endlocal
""";
    }

    private static string BuildInstructions(string masterDwgPath, string scriptPath, string batchPath, string zipPath, CadMergeMode mergeMode)
    {
        string sendStep = mergeMode == CadMergeMode.StandaloneDwg
            ? $"""
4. Để gửi sang máy khác, chỉ cần gửi file DWG độc lập:
   {masterDwgPath}
   Máy nhận mở Revit và link/import trực tiếp file này.
"""
            : $"""
4. Để gửi sang máy khác, gửi file ZIP:
   {zipPath}
   Máy nhận giải nén ZIP, rồi link/import file GOP_CAD_MASTER.dwg trong thư mục vừa giải nén.
""";

        string note = mergeMode == CadMergeMode.StandaloneDwg
            ? """
- File master chèn các DWG con bằng INSERT.
- Đây là file DWG độc lập, gửi riêng GOP_CAD_MASTER.dwg sang máy khác vẫn import/link được trong Revit.
- File có thể nặng hơn và không còn giữ liên kết sống với các DWG con.
"""
            : """
- File master gan cac DWG con bang XREF tai diem 0,0,0, scale 1, rotation 0.
- XREF được lưu bằng đường dẫn tương đối CAD_LINKS\..., nên có thể gửi sang máy khác nếu giữ nguyên cấu trúc thư mục.
- Hãy giữ nguyên thư mục CAD_LINKS nằm cạnh file GOP_CAD_MASTER.dwg để Revit/AutoCAD tìm được link.
- Cách này giữ được liên kết và cấu trúc file gốc tốt hơn so với việc bind/nổ từng hình học vào một DWG.
""";

        return $"""
HƯỚNG DẪN GỘP CAD CHO REVIT

1. Chạy file:
   {batchPath}

2. Nếu máy không có AutoCAD Core Console, mở AutoCAD thủ công và chạy lệnh SCRIPT, chọn file:
   {scriptPath}

3. Sau khi tạo xong, trong Revit dùng Link CAD hoặc Import CAD với file:
   {masterDwgPath}

{sendStep}

Lưu ý:
{note}
""";
    }

    private static string QuoteForAutoCad(string value)
    {
        return "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }
}
