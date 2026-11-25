using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using OpenMcdf;

namespace RvtConvert.App
{
    internal sealed class RevitInstall
    {
        public int Year { get; set; }
        public string ExePath { get; set; }
        public string Framework => Year <= 2024 ? "net48" : Year <= 2026 ? "net8" : "net10";
        public override string ToString() => "Revit " + Year;
    }

    internal static class RevitLocator
    {
        public const int MinYear = 2024;

        /// <summary>Các bản Revit 2024+ cài trên máy (có Revit.exe) và có payload add-in tương ứng trong exe.</summary>
        public static List<RevitInstall> Detect()
        {
            var list = new List<RevitInstall>();
            var roots = new[]
            {
                Environment.GetEnvironmentVariable("ProgramW6432"),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            }.Where(r => !string.IsNullOrEmpty(r)).Distinct();

            foreach (string root in roots)
            {
                string autodesk = Path.Combine(root, "Autodesk");
                if (!Directory.Exists(autodesk)) continue;
                foreach (string dir in Directory.GetDirectories(autodesk, "Revit*"))
                {
                    var m = Regex.Match(Path.GetFileName(dir), @"(20\d\d)");
                    if (!m.Success) continue;
                    int year = int.Parse(m.Groups[1].Value);
                    string exe = Path.Combine(dir, "Revit.exe");
                    if (year < MinYear || !File.Exists(exe) || list.Any(x => x.Year == year)) continue;
                    var inst = new RevitInstall { Year = year, ExePath = exe };
                    if (Assembly.GetExecutingAssembly().GetManifestResourceInfo(inst.Framework + ".BIMLab.RvtConvert.dll") == null)
                        continue; // bản exe này không kèm payload cho phiên bản đó
                    list.Add(inst);
                }
            }
            return list.OrderBy(x => x.Year).ToList();
        }
    }

    internal static class RvtVersionReader
    {
        /// <summary>Đọc version của file .rvt/.rfa từ luồng BasicFileInfo (không cần mở Revit). Trả về 0 nếu không đọc được.</summary>
        public static int ReadYear(string path, out string build)
        {
            build = "";
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var cf = new CompoundFile(fs))
                {
                    byte[] data = cf.RootStorage.GetStream("BasicFileInfo").GetData();
                    foreach (string text in Decodings(data))
                    {
                        var f = Regex.Match(text, @"Format:\s*(20\d\d)");
                        if (!f.Success) continue;
                        var b = Regex.Match(text, @"Revit Build:\s*([^\r\n]+)");
                        if (b.Success) build = b.Groups[1].Value.Trim();
                        return int.Parse(f.Groups[1].Value);
                    }
                }
            }
            catch { }
            return 0;
        }

        private static IEnumerable<string> Decodings(byte[] d)
        {
            yield return Encoding.Unicode.GetString(d);
            if (d.Length > 1) yield return Encoding.Unicode.GetString(d, 1, d.Length - 1);
            yield return Encoding.UTF8.GetString(d);
        }
    }

    internal sealed class JobItem
    {
        public string Source, Target;
        public int Index;
        public object Tag;
    }

    internal sealed class JobOptions
    {
        public RevitInstall Revit;
        public bool Detach = true, Compact, Overwrite = true, CloseRevit = true;
        public string Mode = "upgrade"; // upgrade | ifcexport | ifcimport
    }

    /// <summary>Đăng ký add-in tạm, khởi động Revit đích với file job, theo dõi kết quả rồi dọn dẹp.</summary>
    internal static class JobRunner
    {
        private const string AddInId = "3C1B2E77-5A0D-4E8B-9C45-6B7D2F1A9E10";
        private const string ManifestName = "BIMLabRvtConvert.addin";

        private static string DataRoot => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BIMLab", "RvtConvert");

        // Revit 2027 changed the all-users location. The per-user location
        // still works and lets Revit start under the current Windows account.
        private static string ManifestDir(int year) =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"Autodesk\Revit\Addins", year.ToString());

        /// <summary>Xóa manifest còn sót (nếu tool trước đó bị tắt giữa chừng) để Revit khỏi hỏi add-in lạ.</summary>
        public static void CleanupStale()
        {
            for (int y = RevitLocator.MinYear; y <= 2030; y++)
                TryDelete(Path.Combine(ManifestDir(y), ManifestName));
        }

        public static async Task<List<string>> RunAsync(IList<JobItem> items, JobOptions opt,
            Action<string> log, Action<JobItem, string, string> onItem, CancellationToken ct)
        {
            var errors = new List<string>();
            string work = Path.Combine(Path.GetTempPath(), "BIMLabRvtConvert", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            string jobFile = Path.Combine(work, "job.txt");
            string resultFile = Path.Combine(work, "result.txt");
            string logFile = Path.Combine(work, "addin.log");
            File.WriteAllText(resultFile, "");

            string manifest = Path.Combine(ManifestDir(opt.Revit.Year), ManifestName);
            Process revit = null;
            try
            {
                string dll = ExtractAddin(opt.Revit);
                WriteManifest(manifest, dll);

                var sb = new StringBuilder();
                sb.AppendLine("mode=" + opt.Mode);
                sb.AppendLine("result=" + resultFile);
                sb.AppendLine("log=" + logFile);
                sb.AppendLine("detach=" + (opt.Detach ? "1" : "0"));
                sb.AppendLine("compact=" + (opt.Compact ? "1" : "0"));
                sb.AppendLine("overwrite=" + (opt.Overwrite ? "1" : "0"));
                sb.AppendLine("closeRevit=" + (opt.CloseRevit ? "1" : "0"));
                foreach (var it in items) sb.AppendLine("item\t" + it.Source + "\t" + it.Target);
                File.WriteAllText(jobFile, sb.ToString(), new UTF8Encoding(false));

                var psi = new ProcessStartInfo(opt.Revit.ExePath, "/nosplash")
                {
                    UseShellExecute = false,
                    WorkingDirectory = Path.GetDirectoryName(opt.Revit.ExePath),
                };
                psi.EnvironmentVariables["BIMLAB_RVTCONVERT_JOB"] = jobFile;
                log("Đang khởi động Revit " + opt.Revit.Year + " (có thể mất 1-2 phút)...");
                revit = Process.Start(psi);

                int done = 0;
                bool finished = false;
                long pos = 0;
                var lastActivity = DateTime.UtcNow;
                var lastClick = DateTime.MinValue;

                while (!finished)
                {
                    ct.ThrowIfCancellationRequested();
                    await Task.Delay(700, ct).ConfigureAwait(false);

                    if (DateTime.UtcNow - lastClick > TimeSpan.FromSeconds(2))
                    {
                        lastClick = DateTime.UtcNow;
                        if (TryAcceptUnsignedAddin(revit.Id))
                            log("Đã tự bấm 'Always Load' cho hộp thoại bảo mật add-in của Revit.");
                    }

                    foreach (string line in ReadNewLines(resultFile, ref pos))
                    {
                        lastActivity = DateTime.UtcNow;
                        var p = line.Split('\t');
                        switch (p[0])
                        {
                            case "READY": log("Revit đã sẵn sàng, bắt đầu chuyển đổi..."); break;
                            case "START":
                                onItem(items[int.Parse(p[1])], "Đang chuyển...", null);
                                break;
                            case "OK":
                                done++;
                                onItem(items[int.Parse(p[1])], "Xong (" + p[3] + "s)", "ok");
                                break;
                            case "ERR":
                                done++;
                                errors.Add(items[int.Parse(p[1])].Source + ": " + p[2]);
                                onItem(items[int.Parse(p[1])], "LỖI: " + p[2], "err");
                                break;
                            case "FATAL": errors.Add("Lỗi chung: " + p[1]); break;
                            case "DONE": finished = true; break;
                        }
                    }

                    if (!finished && revit.HasExited)
                    {
                        // Revit tắt sớm (người dùng đóng, hoặc add-in bị chặn): đọc nốt rồi dừng.
                        foreach (string line in ReadNewLines(resultFile, ref pos))
                            if (line.StartsWith("DONE")) finished = true;
                        if (!finished)
                        {
                            errors.Add("Revit đã đóng trước khi chuyển đổi xong.");
                            break;
                        }
                    }

                    if (DateTime.UtcNow - lastActivity > TimeSpan.FromMinutes(25))
                    {
                        errors.Add("Quá thời gian chờ Revit (25 phút không có phản hồi).");
                        break;
                    }
                }

                if (revit != null && !revit.HasExited && opt.CloseRevit)
                {
                    for (int i = 0; i < 60 && !revit.HasExited; i++) await Task.Delay(1000, ct).ConfigureAwait(false);
                }
            }
            finally
            {
                try { if (revit != null && !revit.HasExited && (opt.CloseRevit || ct.IsCancellationRequested)) revit.Kill(); } catch { }
                TryDelete(manifest);
                string addinLog = File.Exists(logFile) ? File.ReadAllText(logFile) : "";
                if (addinLog.Length > 0) log("--- Nhật ký add-in ---\r\n" + addinLog.Trim());
                try { Directory.Delete(work, true); } catch { }
            }
            return errors;
        }

        private static string ExtractAddin(RevitInstall rv)
        {
            string dir = Path.Combine(DataRoot, rv.Framework);
            Directory.CreateDirectory(dir);
            string dll = Path.Combine(dir, "BIMLab.RvtConvert.dll");
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(rv.Framework + ".BIMLab.RvtConvert.dll"))
            using (var f = File.Create(dll))
                s.CopyTo(f);
            return dll;
        }

        private static void WriteManifest(string path, string dll)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path,
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<RevitAddIns>\r\n  <AddIn Type=\"Application\">\r\n" +
                "    <Name>BIMLab Revit Convert</Name>\r\n    <Assembly>" + dll + "</Assembly>\r\n" +
                "    <AddInId>" + AddInId + "</AddInId>\r\n    <FullClassName>RvtConvert.Addin.App</FullClassName>\r\n" +
                "    <VendorId>BIMLAB</VendorId>\r\n    <VendorDescription>BIMLab</VendorDescription>\r\n" +
                "  </AddIn>\r\n</RevitAddIns>\r\n");
        }

        private static IEnumerable<string> ReadNewLines(string file, ref long pos)
        {
            var lines = new List<string>();
            try
            {
                using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    if (fs.Length <= pos) return lines;
                    fs.Seek(pos, SeekOrigin.Begin);
                    var buf = new byte[fs.Length - pos];
                    int n = fs.Read(buf, 0, buf.Length);
                    string text = Encoding.UTF8.GetString(buf, 0, n);
                    int last = text.LastIndexOf('\n');
                    if (last < 0) return lines; // dòng chưa ghi xong
                    pos += Encoding.UTF8.GetByteCount(text.Substring(0, last + 1));
                    foreach (string l in text.Substring(0, last + 1).Split('\n'))
                        if (l.Trim().Length > 0) lines.Add(l.TrimEnd('\r'));
                }
            }
            catch (IOException) { }
            return lines;
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        /// <summary>
        /// Add-in chưa ký -> Revit hỏi "Security - Unsigned Add-In". Tìm hộp thoại đó của đúng tiến trình Revit
        /// và bấm "Always Load" (UI Automation). Chỉ hoạt động với giao diện Revit tiếng Anh.
        /// </summary>
        private static bool TryAcceptUnsignedAddin(int pid)
        {
            try
            {
                var cond = new AndCondition(
                    new PropertyCondition(AutomationElement.ProcessIdProperty, pid),
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window));
                foreach (AutomationElement w in AutomationElement.RootElement.FindAll(TreeScope.Children, cond))
                {
                    string name = w.Current.Name ?? "";
                    if (name.IndexOf("Unsigned", StringComparison.OrdinalIgnoreCase) < 0 &&
                        name.IndexOf("Security", StringComparison.OrdinalIgnoreCase) < 0) continue;

                    foreach (AutomationElement el in w.FindAll(TreeScope.Descendants, Condition.TrueCondition))
                    {
                        string n = el.Current.Name ?? "";
                        if (n.IndexOf("Always Load", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        object pat;
                        if (el.TryGetCurrentPattern(InvokePattern.Pattern, out pat))
                        {
                            ((InvokePattern)pat).Invoke();
                            return true;
                        }
                    }
                }
            }
            catch { }
            return false;
        }
    }
}
