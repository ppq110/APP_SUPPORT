using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;

namespace GopCadApp;

internal sealed class MainForm : Form
{
    private readonly PictureBox _logoBox = new() { SizeMode = PictureBoxSizeMode.Zoom };
    private readonly TextBox _sourceBox = new() { Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top };
    private readonly TextBox _outputBox = new() { Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top };
    private string? _autoCadConsolePath;
    private readonly Button _pickFolderButton = new() { Text = "Thư mục", Width = 82 };
    private readonly Button _pickFilesButton = new() { Text = "File CAD", Width = 82 };
    private readonly Button _classifyButton = new() { Text = "Phân loại", Width = 86, Enabled = false };
    private readonly CheckBox _includeSubfolders = new() { Text = "Lấy cả file DWG/BAK trong thư mục con", AutoSize = true };
    private readonly Button _createButton = new() { Text = "Tạo 1 file DWG", Height = 36, Anchor = AnchorStyles.Right | AnchorStyles.Top };
    private readonly Label _progressLabel = new() { Text = "Sẵn sàng", AutoSize = true };
    private readonly ProgressBar _progressBar = new() { Minimum = 0, Maximum = 100, Value = 0, Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top };
    private readonly Button _detailsButton = new() { Text = "Chi tiết", Width = 90, Height = 28, Anchor = AnchorStyles.Right | AnchorStyles.Top };
    private readonly TextBox _logBox = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top | AnchorStyles.Bottom };
    private readonly List<DwgSourceItem> _sourceItems = [];
    private bool _detailsVisible;
    private bool _busy;

    public MainForm()
    {
        Text = "BIMLab DWG Merge";
        Width = 860;
        Height = 470;
        MinimumSize = new Size(760, 420);
        StartPosition = FormStartPosition.CenterScreen;
        LoadIconAndLogo();

        string defaultOutput = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "GOP_CAD_OUTPUT");
        _outputBox.Text = defaultOutput;
        _autoCadConsolePath = DwgPackager.FindAutoCadCoreConsole();

        BuildLayout();

        Shown += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_autoCadConsolePath))
            {
                SetStatus("Chưa tìm thấy AutoCAD. Hãy kiểm tra máy đã cài AutoCAD.");
            }
            else
            {
                SetStatus("Sẵn sàng");
            }
        };
    }

    private void LoadIconAndLogo()
    {
        Icon? appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        if (appIcon is not null)
        {
            Icon = appIcon;
        }

        using Stream? logoStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("GopCadApp.Resources.bimlab-logo-light-wide.png");
        if (logoStream is not null)
        {
            _logoBox.Image = Image.FromStream(logoStream);
        }
    }

    private void BuildLayout()
    {
        Padding = new Padding(14);

        _logoBox.Left = 20;
        _logoBox.Top = 14;
        _logoBox.Width = 180;
        _logoBox.Height = 44;

        Label title = new()
        {
            Text = "Gộp nhiều file DWG thành 1 file DWG",
            Font = new Font(Font.FontFamily, 12, FontStyle.Bold),
            AutoSize = true,
            Left = 220,
            Top = 16
        };

        Label subtitle = new()
        {
            Text = "Tạo master DWG, tách sheet/layout và chuẩn bị file để import/link vào Revit",
            AutoSize = true,
            Left = 220,
            Top = 42
        };

        AddSourceRow(82);
        AddPathRow("Thư mục xuất kết quả", _outputBox, BrowseOutput, 142);

        _includeSubfolders.Left = 14;
        _includeSubfolders.Top = 214;
        _includeSubfolders.Checked = true;

        _createButton.Top = 202;
        _createButton.Width = 140;
        _createButton.Left = ClientSize.Width - _createButton.Width - 14;
        _createButton.Click += (_, _) => CreatePackage();

        _progressLabel.Left = 14;
        _progressLabel.Top = 260;

        _progressBar.Left = 14;
        _progressBar.Top = 284;
        _progressBar.Width = ClientSize.Width - 132;
        _progressBar.Height = 20;

        _detailsButton.Left = ClientSize.Width - 104;
        _detailsButton.Top = 278;
        _detailsButton.Click += (_, _) => ToggleDetails();

        _logBox.Left = 14;
        _logBox.Top = 314;
        _logBox.Width = ClientSize.Width - 28;
        _logBox.Height = 120;
        _logBox.Visible = false;

        Controls.AddRange([_logoBox, title, subtitle, _includeSubfolders, _createButton, _progressLabel, _progressBar, _detailsButton, _logBox]);
        Resize += (_, _) =>
        {
            _createButton.Left = ClientSize.Width - _createButton.Width - 14;
            _progressBar.Width = ClientSize.Width - 132;
            _detailsButton.Left = ClientSize.Width - 104;
            _logBox.Width = ClientSize.Width - 28;
            _logBox.Height = ClientSize.Height - _logBox.Top - 14;
        };
    }

    private void AddSourceRow(int top)
    {
        Label label = new() { Text = "Nguồn DWG/BAK", Left = 14, Top = top, AutoSize = true };
        _sourceBox.Left = 14;
        _sourceBox.Top = top + 24;
        _sourceBox.Width = ClientSize.Width - 296;
        _sourceBox.ReadOnly = true;

        _pickFilesButton.Left = ClientSize.Width - 276;
        _pickFilesButton.Top = top + 22;
        _pickFilesButton.Height = _sourceBox.Height + 2;
        _pickFilesButton.Anchor = AnchorStyles.Right | AnchorStyles.Top;
        _pickFilesButton.Click += (_, _) => BrowseDwgFiles();

        _pickFolderButton.Left = ClientSize.Width - 188;
        _pickFolderButton.Top = top + 22;
        _pickFolderButton.Height = _sourceBox.Height + 2;
        _pickFolderButton.Anchor = AnchorStyles.Right | AnchorStyles.Top;
        _pickFolderButton.Click += BrowseSource;

        _classifyButton.Left = ClientSize.Width - 100;
        _classifyButton.Top = top + 22;
        _classifyButton.Height = _sourceBox.Height + 2;
        _classifyButton.Anchor = AnchorStyles.Right | AnchorStyles.Top;
        _classifyButton.Click += (_, _) => EditClassification();

        Controls.Add(label);
        Controls.Add(_sourceBox);
        Controls.Add(_pickFilesButton);
        Controls.Add(_pickFolderButton);
        Controls.Add(_classifyButton);

        Resize += (_, _) =>
        {
            _sourceBox.Width = ClientSize.Width - 296;
            _pickFilesButton.Left = ClientSize.Width - 276;
            _pickFolderButton.Left = ClientSize.Width - 188;
            _classifyButton.Left = ClientSize.Width - 100;
        };
    }

    private void AddPathRow(string labelText, TextBox textBox, EventHandler browseHandler, int top)
    {
        Label label = new() { Text = labelText, Left = 14, Top = top, AutoSize = true };
        textBox.Left = 14;
        textBox.Top = top + 24;
        textBox.Width = ClientSize.Width - 112;

        Button browse = new() { Text = "...", Left = ClientSize.Width - 84, Top = top + 22, Width = 70, Height = textBox.Height + 2, Anchor = AnchorStyles.Right | AnchorStyles.Top };
        browse.Click += browseHandler;

        Controls.Add(label);
        Controls.Add(textBox);
        Controls.Add(browse);

        Resize += (_, _) => textBox.Width = ClientSize.Width - 112;
    }

    private void BrowseSource(object? sender, EventArgs e)
    {
        using FolderBrowserDialog dialog = new() { Description = "Chọn thư mục chứa các file DWG export từ Revit" };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            List<string> dwgs = Directory
                .EnumerateFiles(dialog.SelectedPath, "*.*", _includeSubfolders.Checked ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                .Where(DwgPackager.IsCadDrawingSource)
                .Where(path => !Path.GetFileName(path).StartsWith("GOP_CAD_MASTER", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            if (!LoadSourceItems(dwgs))
            {
                return;
            }

            _sourceBox.Text = dialog.SelectedPath;
            _includeSubfolders.Enabled = true;
        }
    }

    private void BrowseDwgFiles()
    {
        using OpenFileDialog dialog = new()
        {
            Title = "Chọn một vài file DWG/BAK để test nhanh",
            Filter = "CAD drawings|*.dwg;*.bak|DWG files|*.dwg|BAK files|*.bak|All files|*.*",
            Multiselect = true
        };

        if (Directory.Exists(_sourceBox.Text))
        {
            dialog.InitialDirectory = _sourceBox.Text;
        }

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            if (!LoadSourceItems(dialog.FileNames))
            {
                return;
            }

            _sourceBox.Text = _sourceItems.Count == 1
                ? _sourceItems[0].Path
                : $"Đã chọn {_sourceItems.Count} file CAD";
            _includeSubfolders.Enabled = false;
        }
    }

    private bool LoadSourceItems(IEnumerable<string> paths)
    {
        List<string> candidates = paths
            .Where(DwgPackager.IsCadDrawingSource)
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // AutoCAD tự tạo X.bak (bản lưu cũ) cạnh X.dwg: bỏ bản .bak để mỗi bản vẽ chỉ được gộp một lần.
        candidates = DwgPackager.RemoveBackupDuplicates(candidates, out int duplicateBackups);

        List<DwgSourceItem> items = candidates
            .Select(path => new DwgSourceItem(path, DwgSourceKind.DirectMerge))
            .ToList();

        if (items.Count == 0)
        {
            MessageBox.Show(this, "Không tìm thấy file DWG/BAK nào để gộp.", "BIMLab DWG Merge", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }

        using SourceClassificationForm dialog = new(items);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return false;
        }

        _sourceItems.Clear();
        _sourceItems.AddRange(dialog.Items);
        _classifyButton.Enabled = _sourceItems.Count > 0;

        string status = $"Đã chọn {_sourceItems.Count} file CAD.";
        if (duplicateBackups > 0)
        {
            status += $" Đã bỏ {duplicateBackups} file .bak trùng tên với file .dwg.";
            Log($"Bỏ {duplicateBackups} file .bak trùng tên với file .dwg (bản lưu cũ do AutoCAD tự tạo).");
        }

        SetStatus(status);
        return true;
    }

    private void EditClassification()
    {
        if (_sourceItems.Count == 0)
        {
            MessageBox.Show(this, "Chưa chọn file DWG/BAK nào.", "BIMLab DWG Merge", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using SourceClassificationForm dialog = new(_sourceItems);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _sourceItems.Clear();
            _sourceItems.AddRange(dialog.Items);
            SetStatus($"Đã phân loại {_sourceItems.Count} file CAD.");
        }
    }

    private void BrowseOutput(object? sender, EventArgs e)
    {
        using FolderBrowserDialog dialog = new() { Description = "Chọn thư mục để xuất kết quả" };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _outputBox.Text = dialog.SelectedPath;
        }
    }

    private async void CreatePackage()
    {
        DwgPackResult? result = null;
        try
        {
            SetBusy(true);
            ResetProgress();
            if (_sourceItems.Count == 0)
            {
                throw new InvalidOperationException("Chưa chọn nguồn DWG/BAK.");
            }

            string outputRoot = _outputBox.Text.Trim();
            if (outputRoot.Length == 0)
            {
                throw new InvalidOperationException("Chưa chọn thư mục xuất kết quả.");
            }

            _autoCadConsolePath ??= DwgPackager.FindAutoCadCoreConsole();
            if (string.IsNullOrWhiteSpace(_autoCadConsolePath))
            {
                throw new InvalidOperationException("Không tìm thấy AutoCAD Core Console. Hãy cài AutoCAD trên máy này rồi chạy lại app.");
            }

            // Chụp lại danh sách file, đường dẫn xuất và AutoCAD trước khi chạy nền: luồng nền không được
            // đọc điều khiển giao diện, và danh sách gốc không được thay đổi giữa chừng.
            List<DwgSourceItem> items = [.. _sourceItems];
            string autoCadPath = _autoCadConsolePath;

            Log("Đang tạo gói CAD...");
            SetStatus("Đang chuẩn bị dữ liệu...");
            CadMergeMode mergeMode = CadMergeMode.StandaloneDwg;
            Progress<CadProgress> progress = new(UpdateProgress);

            result = await Task.Run(() => DwgPackager.CreatePackage(items, outputRoot, autoCadPath, mergeMode, progress));

            Log($"Đã copy {result.DwgCount} file DWG.");
            Log("Đã tạo script AutoCAD.");

            Log("Đang chạy AutoCAD Core Console để tạo 1 file DWG...");
            UpdateProgress(new CadProgress(0, result.DwgCount, "AutoCAD đang bắt đầu", string.Empty));
            await DwgPackager.RunAutoCadScriptAsync(autoCadPath, result.InputDrawingPath, result.ScriptPath, result.ConsoleLogPath, result.MasterDwgPath, progress, Log);

            if (!File.Exists(result.MasterDwgPath))
            {
                Log($"AutoCAD đã chạy xong nhưng chưa thấy file master DWG. Hãy kiểm tra log: {result.ConsoleLogPath}");
                SetStatus("Chưa thấy file master DWG. Bấm Chi tiết để xem log.");
                return;
            }

            // Đưa master ra thư mục xuất, ghi file danh sách đối chiếu Sheet_xxx <-> file gốc, dọn thư mục làm việc.
            (string masterPath, string? listPath) = DwgPackager.PublishResult(result);
            UpdateProgress(new CadProgress(result.DwgCount, result.DwgCount, "Hoàn thành", Path.GetFileName(masterPath)));
            SetStatus($"Hoàn thành: {Path.GetFileName(masterPath)}");
            Log($"Đã tạo file DWG: {masterPath}");
            Log(listPath is null
                ? "Không ghi được file danh sách đối chiếu Sheet với file gốc."
                : $"Danh sách đối chiếu Sheet_xxx với file gốc: {listPath}");

            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{masterPath}\"") { UseShellExecute = true });
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
            {
                Log("Không mở được thư mục kết quả: " + ex.Message);
            }
        }
        catch (Exception ex)
        {
            string detail = ex.Message;
            if (result is not null && Directory.Exists(result.PackageFolder))
            {
                detail += Environment.NewLine + Environment.NewLine + "Thư mục làm việc (có log AutoCAD): " + result.PackageFolder;
            }

            MessageBox.Show(this, detail, "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
            SetStatus("Có lỗi xảy ra. Bấm Chi tiết để xem log.");
            Log("LỖI: " + ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    // Trong lúc gộp, khoá các nút đổi nguồn/phân loại: danh sách file không được đổi giữa chừng.
    private void SetBusy(bool busy)
    {
        _busy = busy;
        _createButton.Enabled = !busy;
        _pickFilesButton.Enabled = !busy;
        _pickFolderButton.Enabled = !busy;
        _classifyButton.Enabled = !busy && _sourceItems.Count > 0;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_busy && e.CloseReason == CloseReason.UserClosing)
        {
            DialogResult answer = MessageBox.Show(
                this,
                "Đang gộp file. Thoát bây giờ sẽ bỏ dở và AutoCAD có thể còn chạy nền thêm một lúc.\n\nVẫn thoát?",
                "BIMLab DWG Merge",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes)
            {
                e.Cancel = true;
                return;
            }
        }

        base.OnFormClosing(e);
    }

    // Có thể bị gọi từ luồng đọc output của AutoCAD (không phải luồng giao diện) nên phải chuyển về luồng UI.
    private void Log(string message)
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            try
            {
                BeginInvoke(new Action(() => Log(message)));
            }
            catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
            {
                // Cửa sổ đã đóng: bỏ qua dòng log.
            }

            return;
        }

        _logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
    }

    private void ResetProgress()
    {
        _progressBar.Style = ProgressBarStyle.Continuous;
        _progressBar.Value = 0;
        SetStatus("Đang chuẩn bị...");
    }

    private void UpdateProgress(CadProgress progress)
    {
        int total = Math.Max(progress.Total, 1);
        int current = Math.Clamp(progress.Current, 0, total);
        int percent = (int)Math.Round(current * 100.0 / total);

        _progressBar.Value = Math.Clamp(percent, 0, 100);
        SetStatus(string.IsNullOrWhiteSpace(progress.FileName)
            ? $"{progress.Stage}: {current}/{total}"
            : $"{progress.Stage}: {current}/{total} - {progress.FileName}");
    }

    private void SetStatus(string message)
    {
        _progressLabel.Text = message;
    }

    private void ToggleDetails()
    {
        _detailsVisible = !_detailsVisible;
        _logBox.Visible = _detailsVisible;
        _detailsButton.Text = _detailsVisible ? "Ẩn chi tiết" : "Chi tiết";
        Height = _detailsVisible ? 600 : 470;
    }
}
