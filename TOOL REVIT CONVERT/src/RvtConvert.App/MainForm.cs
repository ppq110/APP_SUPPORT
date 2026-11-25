using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace RvtConvert.App
{
    internal sealed class FileEntry
    {
        public string Path;
        public int Year;
        public string Build;
        public ListViewItem Row;
    }

    public class MainForm : Form
    {
        private static readonly Color HeaderColor = Color.FromArgb(18, 90, 175);
        private static readonly Color ToolbarColor = Color.FromArgb(244, 246, 251);
        private static readonly Color GrayText = Color.FromArgb(52, 72, 108);
        private static readonly Color GreenButton = Color.FromArgb(34, 126, 68);

        private readonly ListView _list = new ListView();
        private readonly Label _dropHint = new Label();
        private readonly ComboBox _target = new ComboBox();
        private readonly TextBox _outDir = new TextBox();
        private readonly TextBox _suffix = new TextBox { Text = "_R{year}" };
        private readonly CheckBox _chkDown = new CheckBox { Text = "Cho phép HẠ version qua IFC (giữ hình khối, mất family/thông số)", Checked = true, AutoSize = true };
        private readonly CheckBox _chkOverwrite = new CheckBox { Text = "Ghi đè file đích nếu đã có", Checked = true, AutoSize = true };
        private readonly CheckBox _chkDetach = new CheckBox { Text = "Tách khỏi Central (file workshare)", Checked = true, AutoSize = true };
        private readonly CheckBox _chkCompact = new CheckBox { Text = "Nén file khi lưu", AutoSize = true };
        private readonly CheckBox _chkClose = new CheckBox { Text = "Tự đóng Revit khi xong", Checked = true, AutoSize = true };
        private readonly CheckBox _chkSub = new CheckBox { Text = "Gồm thư mục con", Checked = true, AutoSize = true };
        private readonly Button _btnRun = new Button();
        private readonly Button _btnCancel = new Button();
        private readonly Button _btnOpen = new Button();
        private readonly ProgressBar _bar = new ProgressBar();
        private readonly TextBox _log = new TextBox();
        private readonly Label _found = new Label();
        private readonly List<FileEntry> _files = new List<FileEntry>();
        private List<RevitInstall> _installs = new List<RevitInstall>();
        private CancellationTokenSource _cts;
        private string _lastOutDir;

        private static readonly string SettingsFile = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BIMLab", "RvtConvert", "settings.txt");

        public MainForm(string[] startPaths)
        {
            Text = "BIMLab Revit Convert";
            Size = new Size(1020, 780);
            MinimumSize = new Size(920, 700);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9.5f);
            BackColor = Color.White;
            AllowDrop = true;
            DragEnter += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
            DragDrop += (s, e) => AddPaths((string[])e.Data.GetData(DataFormats.FileDrop));

            BuildLayout();
            _installs = RevitLocator.Detect();
            foreach (var i in _installs) _target.Items.Add(i);
            if (_target.Items.Count > 0) _target.SelectedIndex = _target.Items.Count - 1;

            LoadSettings();
            _target.SelectedIndexChanged += (s, a) => RefreshStatus();
            FormClosing += (s, e) => SaveSettings();

            if (_installs.Count == 0)
            {
                _found.Text = "Không tìm thấy Revit 2024 trở lên trên máy này.";
                _found.ForeColor = Color.Firebrick;
                Log("Không tìm thấy Revit 2024+ (cần có Revit.exe trong Program Files\\Autodesk\\Revit 20xx). Tool vẫn đọc được version file, nhưng chưa chuyển đổi được.");
            }
            else
            {
                _found.Text = "Revit tìm thấy trên máy: " + string.Join(", ", _installs.Select(i => i.Year.ToString()));
            }

            try { JobRunner.CleanupStale(); } catch { }
            Log("Cách dùng: kéo thả file .rvt/.rfa (hoặc thư mục) vào cửa sổ -> chọn Revit đích -> bấm Chuyển đổi.");

            if (startPaths != null && startPaths.Length > 0)
                Shown += (s, e) => AddPaths(startPaths);
        }

        private void BuildLayout()
        {
            var header = new Panel { Dock = DockStyle.Top, Height = 68, BackColor = HeaderColor };
            try
            {
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("RvtConvert.logo.png"))
                {
                    var wrap = new Panel { Bounds = new Rectangle(16, 12, 44, 44), BackColor = Color.White };
                    wrap.Controls.Add(new PictureBox
                    {
                        Image = Image.FromStream(s), SizeMode = PictureBoxSizeMode.Zoom,
                        Bounds = new Rectangle(3, 3, 38, 38), BackColor = Color.White,
                    });
                    header.Controls.Add(wrap);
                }
            }
            catch { }
            header.Controls.Add(new Label
            {
                Text = "BIMLab Revit Convert", Font = new Font("Segoe UI", 13f, FontStyle.Bold),
                ForeColor = Color.White, AutoSize = true, Location = new Point(72, 12),
            });
            _found.AutoSize = true; _found.Location = new Point(74, 40);
            _found.Font = new Font("Segoe UI", 9f); _found.ForeColor = Color.FromArgb(190, 215, 255);
            header.Controls.Add(_found);

            var bar1 = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 46, Padding = new Padding(12, 8, 12, 4), BackColor = ToolbarColor };
            var bAdd = Btn("Thêm file...", 110); bAdd.Click += (s, e) => PickFiles();
            var bDir = Btn("Thêm thư mục...", 130); bDir.Click += (s, e) => PickFolder();
            var bDel = Btn("Xóa chọn", 90); bDel.Click += (s, e) => RemoveSelected();
            var bClr = Btn("Xóa hết", 80); bClr.Click += (s, e) => ClearAll();
            _chkSub.Margin = new Padding(14, 8, 0, 0);
            bar1.Controls.AddRange(new Control[] { bAdd, bDir, bDel, bClr, _chkSub });

            _list.Dock = DockStyle.Fill;
            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.GridLines = true;
            _list.HideSelection = false;
            _list.Columns.Add("Tên file", 270);
            _list.Columns.Add("Version file", 90);
            _list.Columns.Add("Dung lượng", 90);
            _list.Columns.Add("Trạng thái", 330);
            _list.Columns.Add("Đường dẫn", 400);
            _list.KeyDown += (s, e) => { if (e.KeyCode == Keys.Delete) RemoveSelected(); };

            _dropHint.Dock = DockStyle.Fill;
            _dropHint.TextAlign = ContentAlignment.MiddleCenter;
            _dropHint.Font = new Font("Segoe UI", 15f);
            _dropHint.ForeColor = Color.FromArgb(130, 150, 185);
            _dropHint.BackColor = Color.White;
            _dropHint.Text = "Kéo thả file .rvt / .rfa hoặc cả thư mục vào đây\r\n\r\n(hoặc bấm \"Thêm file...\" / \"Thêm thư mục...\")";

            var settings = new TableLayoutPanel
            {
                Dock = DockStyle.Bottom, Height = 178, Padding = new Padding(12, 8, 12, 4),
                BackColor = ToolbarColor, ColumnCount = 4, RowCount = 5,
            };
            settings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
            settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            settings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 95));
            settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

            _target.DropDownStyle = ComboBoxStyle.DropDownList; _target.Width = 150;
            _outDir.Dock = DockStyle.Fill;
            var bBrowse = Btn("...", 40); bBrowse.Dock = DockStyle.Right; bBrowse.Click += (s, e) => BrowseOut();
            var outPanel = new Panel { Dock = DockStyle.Fill, Height = 30 };
            outPanel.Controls.Add(_outDir); outPanel.Controls.Add(bBrowse);
            _suffix.Dock = DockStyle.Fill;

            settings.Controls.Add(Lbl("Revit đích:"), 0, 0);
            settings.Controls.Add(_target, 1, 0);
            settings.Controls.Add(Lbl("Hậu tố tên:"), 2, 0);
            settings.Controls.Add(_suffix, 3, 0);
            settings.Controls.Add(Lbl("Thư mục xuất:"), 0, 1);
            settings.Controls.Add(outPanel, 1, 1);
            settings.SetColumnSpan(outPanel, 3);
            var hint = new Label
            {
                Text = "Để trống = lưu vào thư mục con \"Revit_<năm>\" cạnh từng file gốc. {year} trong hậu tố = năm Revit đích. File gốc không bị sửa.",
                ForeColor = Color.Gray, AutoSize = true, Font = new Font("Segoe UI", 8.5f),
            };
            settings.Controls.Add(hint, 1, 2);
            settings.SetColumnSpan(hint, 3);
            settings.Controls.Add(_chkDown, 0, 3);
            settings.SetColumnSpan(_chkDown, 4);
            var opts = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
            opts.Controls.AddRange(new Control[] { _chkOverwrite, _chkDetach, _chkCompact, _chkClose });
            settings.Controls.Add(opts, 0, 4);
            settings.SetColumnSpan(opts, 4);

            var runBar = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = Color.White, Padding = new Padding(12, 8, 12, 8) };
            _btnRun.Text = "Chuyển đổi"; _btnRun.Width = 150; _btnRun.Dock = DockStyle.Right;
            Style(_btnRun, GreenButton, Color.White); _btnRun.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
            _btnRun.Click += async (s, e) => await RunAsync();
            _btnCancel.Text = "Dừng"; _btnCancel.Width = 80; _btnCancel.Dock = DockStyle.Right; _btnCancel.Enabled = false;
            Style(_btnCancel, Color.FromArgb(218, 226, 242), GrayText); _btnCancel.Click += (s, e) => _cts?.Cancel();
            _btnOpen.Text = "Mở thư mục kết quả"; _btnOpen.Width = 170; _btnOpen.Dock = DockStyle.Right; _btnOpen.Enabled = false;
            Style(_btnOpen, Color.FromArgb(218, 226, 242), GrayText);
            _btnOpen.Click += (s, e) => { try { if (_lastOutDir != null) Process.Start("explorer.exe", _lastOutDir); } catch { } };
            _bar.Dock = DockStyle.Fill; _bar.Style = ProgressBarStyle.Continuous;
            runBar.Controls.Add(_bar);
            runBar.Controls.Add(new Panel { Dock = DockStyle.Right, Width = 8 });
            runBar.Controls.Add(_btnOpen);
            runBar.Controls.Add(new Panel { Dock = DockStyle.Right, Width = 8 });
            runBar.Controls.Add(_btnCancel);
            runBar.Controls.Add(new Panel { Dock = DockStyle.Right, Width = 8 });
            runBar.Controls.Add(_btnRun);

            _log.Dock = DockStyle.Bottom; _log.Height = 120; _log.Multiline = true; _log.ReadOnly = true;
            _log.ScrollBars = ScrollBars.Vertical; _log.Font = new Font("Consolas", 9f);
            _log.BackColor = Color.FromArgb(250, 251, 253);

            Controls.Add(_list);
            Controls.Add(_dropHint);
            Controls.Add(_log);
            Controls.Add(runBar);
            Controls.Add(settings);
            Controls.Add(bar1);
            Controls.Add(header);
            UpdateHint();
        }

        private void UpdateHint() { _dropHint.Visible = _files.Count == 0; }

        // ---------- thêm / xóa file ----------
        private void PickFiles()
        {
            using (var d = new OpenFileDialog { Multiselect = true, Filter = "Revit (*.rvt;*.rfa)|*.rvt;*.rfa|Tất cả|*.*" })
                if (d.ShowDialog(this) == DialogResult.OK) AddPaths(d.FileNames);
        }

        private void PickFolder()
        {
            using (var d = new FolderBrowserDialog())
                if (d.ShowDialog(this) == DialogResult.OK) AddPaths(new[] { d.SelectedPath });
        }

        private void ClearAll() { _files.Clear(); _list.Items.Clear(); UpdateHint(); }

        private static readonly Regex BackupName = new Regex(@"\.\d{4}\.(rvt|rfa)$", RegexOptions.IgnoreCase);
        private static readonly Regex RevitExt = new Regex(@"\.(rvt|rfa)$", RegexOptions.IgnoreCase);

        private void AddPaths(IEnumerable<string> paths)
        {
            var found = new List<string>();
            foreach (string p in paths)
            {
                if (Directory.Exists(p))
                {
                    var opt = _chkSub.Checked ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                    try { found.AddRange(Directory.EnumerateFiles(p, "*.*", opt).Where(f => RevitExt.IsMatch(f))); }
                    catch (Exception ex) { Log("Không đọc được thư mục " + p + ": " + ex.Message); }
                }
                else if (File.Exists(p) && RevitExt.IsMatch(p))
                    found.Add(p);
            }

            int added = 0, skipped = 0;
            Cursor = Cursors.WaitCursor;
            foreach (string f in found.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (_files.Any(x => string.Equals(x.Path, f, StringComparison.OrdinalIgnoreCase))) continue;
                if (BackupName.IsMatch(f)) { skipped++; continue; } // file backup tự sinh của Revit (xxx.0001.rvt)

                var e = new FileEntry { Path = f };
                e.Year = RvtVersionReader.ReadYear(f, out e.Build);
                long size = 0; try { size = new FileInfo(f).Length; } catch { }
                e.Row = new ListViewItem(new[]
                {
                    System.IO.Path.GetFileName(f),
                    e.Year > 0 ? "Revit " + e.Year : "?",
                    (size / 1048576.0).ToString("0.0") + " MB",
                    "",
                    f,
                });
                _files.Add(e);
                _list.Items.Add(e.Row);
                added++;
            }
            Cursor = Cursors.Default;
            UpdateHint();
            RefreshStatus();
            if (added > 0 || skipped > 0)
                Log("Đã thêm " + added + " file" + (skipped > 0 ? " (bỏ qua " + skipped + " file backup .000x)" : "") + ".");
            else if (found.Count == 0)
                Log("Không tìm thấy file .rvt/.rfa nào trong mục đã chọn.");
        }

        private void RemoveSelected()
        {
            foreach (ListViewItem it in _list.SelectedItems.Cast<ListViewItem>().ToList())
            {
                _files.RemoveAll(x => x.Row == it);
                _list.Items.Remove(it);
            }
            UpdateHint();
        }

        private void BrowseOut()
        {
            using (var d = new FolderBrowserDialog())
                if (d.ShowDialog(this) == DialogResult.OK) _outDir.Text = d.SelectedPath;
        }

        private RevitInstall Target => _target.SelectedItem as RevitInstall;

        /// <summary>Revit thấp nhất (đã cài) đủ mới để mở file năm <paramref name="year"/>.</summary>
        private RevitInstall ExporterFor(int year) => _installs.Where(i => i.Year >= year).OrderBy(i => i.Year).FirstOrDefault();

        /// <summary>Cập nhật cột trạng thái theo Revit đích đang chọn.</summary>
        private void RefreshStatus()
        {
            var t = Target;
            foreach (var f in _files)
            {
                string st = ""; Color c = Color.Black;
                if (t == null) st = "";
                else if (f.Year == 0) st = "Không đọc được version - sẽ thử mở bằng Revit " + t.Year;
                else if (f.Year == t.Year) st = "Cùng version - chỉ lưu lại";
                else if (f.Year < t.Year) st = "Nâng " + f.Year + " → " + t.Year + " (giữ nguyên mô hình)";
                else if (!_chkDown.Checked) { st = "Bỏ qua: file mới hơn Revit đích (bật 'Cho phép HẠ version' để chuyển)"; c = Color.Firebrick; }
                else
                {
                    var ex = ExporterFor(f.Year);
                    if (ex == null) { st = "Không chuyển được: cần cài Revit " + f.Year + " trở lên để đọc file này"; c = Color.Firebrick; }
                    else { st = "Hạ " + f.Year + " → " + t.Year + " qua IFC (dùng Revit " + ex.Year + " để đọc)"; c = Color.DarkOrange; }
                }
                f.Row.SubItems[3].Text = st;
                f.Row.ForeColor = c;
            }
        }

        // ---------- chạy ----------
        private async System.Threading.Tasks.Task RunAsync()
        {
            var rv = Target;
            if (rv == null) { MessageBox.Show(this, "Chưa có Revit đích. Cần cài Revit 2024 trở lên trên máy.", Text); return; }
            if (_files.Count == 0) { MessageBox.Show(this, "Hãy kéo thả file .rvt/.rfa vào cửa sổ trước.", Text); return; }
            RefreshStatus();

            var plan = new List<PlanItem>();
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in _files)
            {
                RevitInstall ex = null;
                if (f.Year > rv.Year)
                {
                    if (!_chkDown.Checked) continue;
                    ex = ExporterFor(f.Year);
                    if (ex == null) continue;
                }

                string dir = string.IsNullOrWhiteSpace(_outDir.Text)
                    ? System.IO.Path.Combine(System.IO.Path.GetDirectoryName(f.Path), "Revit_" + rv.Year)
                    : _outDir.Text;
                string name = System.IO.Path.GetFileNameWithoutExtension(f.Path) +
                              _suffix.Text.Replace("{year}", rv.Year.ToString()) + System.IO.Path.GetExtension(f.Path);
                string target = System.IO.Path.Combine(dir, name);
                for (int n = 2; !used.Add(target); n++)
                    target = System.IO.Path.Combine(dir, System.IO.Path.GetFileNameWithoutExtension(name) + "_" + n + System.IO.Path.GetExtension(name));
                if (string.Equals(target, f.Path, StringComparison.OrdinalIgnoreCase))
                {
                    f.Row.SubItems[3].Text = "Bỏ qua: file đích trùng file gốc (đổi hậu tố hoặc thư mục xuất)";
                    continue;
                }
                plan.Add(new PlanItem { File = f, Final = target, Exporter = ex });
            }
            if (plan.Count == 0) { MessageBox.Show(this, "Không có file nào hợp lệ để chuyển (xem cột Trạng thái).", Text); return; }

            int downCount = plan.Count(p => p.NeedsIfc);
            if (downCount > 0 && MessageBox.Show(this,
                    downCount + " file sẽ được HẠ version qua IFC.\r\n\r\n" +
                    "• Giữ: hình khối, vật liệu cơ bản, level, grid.\r\n" +
                    "• Mất/đổi: family gốc, thông số tùy biến, view, sheet, schedule, annotation.\r\n\r\nTiếp tục?",
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            SaveSettings();
            SetBusy(true);
            _lastOutDir = System.IO.Path.GetDirectoryName(plan[0].Final);
            _bar.Maximum = plan.Count; _bar.Value = 0;
            _cts = new CancellationTokenSource();
            var opt = new JobOptions
            {
                Revit = rv, Detach = _chkDetach.Checked, Compact = _chkCompact.Checked,
                Overwrite = _chkOverwrite.Checked, CloseRevit = _chkClose.Checked,
            };

            List<string> errors;
            try
            {
                errors = await Pipeline.RunAsync(plan, rv, opt,
                    msg => Invoke(new Action(() => Log(msg))),
                    (item, text, kind) => Invoke(new Action(() =>
                    {
                        item.File.Row.SubItems[3].Text = text;
                        item.File.Row.ForeColor = kind == "ok" ? Color.SeaGreen : kind == "err" ? Color.Firebrick : Color.Black;
                    })),
                    () => Invoke(new Action(() => _bar.Value = Math.Min(_bar.Maximum, _bar.Value + 1))),
                    _cts.Token);
            }
            catch (OperationCanceledException) { errors = new List<string> { "Đã dừng theo yêu cầu." }; }
            catch (UnauthorizedAccessException ex)
            {
                errors = new List<string> { "Windows từ chối truy cập. Kiểm tra quyền của tài khoản đang chạy tool. " + ex.Message };
            }
            catch (Exception ex) { errors = new List<string> { ex.Message }; }
            finally { SetBusy(false); }

            int ok = plan.Count(p => p.Done);
            Log("=== Xong: " + ok + "/" + plan.Count + " file thành công" + (errors.Count > 0 ? ", " + errors.Count + " lỗi" : "") + " ===");
            foreach (string e in errors) Log("[LỖI] " + e);
            _btnOpen.Enabled = ok > 0;

            if (ok > 0 && MessageBox.Show(this, "Đã chuyển " + ok + "/" + plan.Count + " file.\r\nMở thư mục kết quả?", Text,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
            {
                try { Process.Start("explorer.exe", _lastOutDir); } catch { }
            }
        }

        private void SetBusy(bool busy)
        {
            _btnRun.Enabled = !busy;
            _btnCancel.Enabled = busy;
            _target.Enabled = !busy;
            _chkDown.Enabled = !busy;
            AllowDrop = !busy;
        }

        // ---------- lưu / nạp cài đặt ----------
        private void SaveSettings()
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(SettingsFile));
                File.WriteAllLines(SettingsFile, new[]
                {
                    "target=" + (Target?.Year.ToString() ?? ""),
                    "outDir=" + _outDir.Text,
                    "suffix=" + _suffix.Text,
                    "down=" + (_chkDown.Checked ? "1" : "0"),
                    "overwrite=" + (_chkOverwrite.Checked ? "1" : "0"),
                    "detach=" + (_chkDetach.Checked ? "1" : "0"),
                    "compact=" + (_chkCompact.Checked ? "1" : "0"),
                    "close=" + (_chkClose.Checked ? "1" : "0"),
                    "sub=" + (_chkSub.Checked ? "1" : "0"),
                });
            }
            catch { }
        }

        private void LoadSettings()
        {
            try
            {
                if (!File.Exists(SettingsFile)) return;
                foreach (string line in File.ReadAllLines(SettingsFile))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = line.Substring(0, eq), v = line.Substring(eq + 1);
                    switch (k)
                    {
                        case "target":
                            for (int i = 0; i < _target.Items.Count; i++)
                                if (((RevitInstall)_target.Items[i]).Year.ToString() == v) _target.SelectedIndex = i;
                            break;
                        case "outDir": _outDir.Text = v; break;
                        case "suffix": if (v.Length > 0) _suffix.Text = v; break;
                        case "down": _chkDown.Checked = v == "1"; break;
                        case "overwrite": _chkOverwrite.Checked = v == "1"; break;
                        case "detach": _chkDetach.Checked = v == "1"; break;
                        case "compact": _chkCompact.Checked = v == "1"; break;
                        case "close": _chkClose.Checked = v == "1"; break;
                        case "sub": _chkSub.Checked = v == "1"; break;
                    }
                }
            }
            catch { }
        }

        // ---------- tiện ích UI ----------
        private void Log(string line)
        {
            _log.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + line + Environment.NewLine);
        }

        private static Label Lbl(string t) =>
            new Label { Text = t, AutoSize = true, ForeColor = GrayText, Margin = new Padding(0, 8, 8, 0), Font = new Font("Segoe UI", 9f, FontStyle.Bold) };

        private static Button Btn(string t, int w)
        {
            var b = new Button { Text = t, Width = w, Height = 30 };
            Style(b, Color.FromArgb(218, 226, 242), GrayText);
            return b;
        }

        private static void Style(Button b, Color bg, Color fg)
        {
            b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderSize = 0;
            b.BackColor = bg; b.ForeColor = fg; b.Cursor = Cursors.Hand;
        }
    }
}
