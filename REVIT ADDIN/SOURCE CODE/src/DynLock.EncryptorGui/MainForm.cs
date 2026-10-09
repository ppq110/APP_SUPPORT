using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DynLock.Core;
using DynLock.EncryptorGui.Auth;

namespace DynLock.EncryptorGui
{
    public class MainForm : Form
    {
        static readonly Color C_Header    = Color.FromArgb(18, 90, 175);
        static readonly Color C_HeaderSub = Color.FromArgb(160, 200, 255);
        static readonly Color C_Toolbar   = Color.FromArgb(244, 246, 251);
        static readonly Color C_Border    = Color.FromArgb(210, 218, 236);
        static readonly Color C_BtnBlue   = Color.FromArgb(30, 120, 215);
        static readonly Color C_BtnGreen  = Color.FromArgb(34, 126, 68);
        static readonly Color C_BtnGray   = Color.FromArgb(218, 226, 242);
        static readonly Color C_GrayFg    = Color.FromArgb(52, 72, 108);
        static readonly Color C_Muted     = Color.FromArgb(108, 124, 152);
        static readonly Color C_StatusBg  = Color.FromArgb(238, 243, 252);

        readonly ListView _list          = new ListView();
        readonly Button   _add           = new Button();
        readonly Button   _edit          = new Button();
        readonly Button   _clear         = new Button();
        readonly Button   _manageLeaders = new Button();
        readonly Button   _run           = new Button();
        readonly Button   _openFolder    = new Button();
        readonly Label    _status        = new Label();
        readonly Label    _emptyHint     = new Label();

        readonly List<DynFileEntry> _entries = new List<DynFileEntry>();
        string _lastOutDir;

        public MainForm()
        {
            Text          = "BIMLab Studio - " + SessionContext.FullName;
            Size          = new Size(900, 560);
            MinimumSize   = new Size(760, 460);
            StartPosition = FormStartPosition.CenterScreen;
            Font          = new Font("Segoe UI", 9.5f);
            BackColor     = Color.White;
            AllowDrop     = true;
            DragEnter    += OnDragEnter;
            DragDrop     += OnDragDrop;

            Controls.Add(BuildListPanel());
            Controls.Add(BuildHeader());
            Controls.Add(new Panel { Dock = DockStyle.Top, Height = 1, BackColor = C_Border });
            Controls.Add(BuildToolbar());
            Controls.Add(BuildBottomBar());
            Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = C_Border });

            _status.Dock      = DockStyle.Bottom;
            _status.Height    = 28;
            _status.Padding   = new Padding(14, 5, 14, 5);
            _status.Font      = new Font("Segoe UI", 9f);
            _status.ForeColor = C_Muted;
            _status.BackColor = C_StatusBg;
            Controls.Add(_status);
        }

        private Panel BuildHeader()
        {
            var header = new Panel { Dock = DockStyle.Top, Height = 76, BackColor = C_Header };
            var iconBmp = LoadBimDynamoIcon(40);
            var iconWrap = new Panel { Bounds = new Rectangle(18, 14, 48, 48), BackColor = Color.White };
            var iconBox = new PictureBox
            {
                Image = iconBmp != null ? (Image)iconBmp : (Image)LogoHelper.GenerateLogo(40),
                SizeMode = PictureBoxSizeMode.Zoom,
                Bounds = new Rectangle(4, 4, 40, 40),
                BackColor = Color.White,
            };
            iconWrap.Controls.Add(iconBox);

            header.Controls.AddRange(new Control[]
            {
                iconWrap,
                new Label
                {
                    Text = "BIMLab Studio",
                    Font = new Font("Segoe UI", 13f, FontStyle.Bold),
                    ForeColor = Color.White,
                    BackColor = Color.Transparent,
                    AutoSize = true,
                    Location = new Point(78, 17),
                },
                new Label
                {
                    Text = "Chọn file .dyn để mã hóa ra .dynx   -   " + SessionContext.Email,
                    Font = new Font("Segoe UI", 8.5f),
                    ForeColor = C_HeaderSub,
                    BackColor = Color.Transparent,
                    AutoSize = true,
                    Location = new Point(80, 46),
                },
            });
            return header;
        }

        private FlowLayoutPanel BuildToolbar()
        {
            var toolbar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 52,
                Padding = new Padding(12, 10, 12, 10),
                BackColor = C_Toolbar,
            };

            Btn(_add, "  Thêm file .dyn", C_BtnBlue, Color.White, 152, 32);
            Btn(_edit, "  Sửa tên/icon", C_BtnGray, C_GrayFg, 126, 32);
            Btn(_clear, "x  Xóa danh sách", C_BtnGray, C_GrayFg, 126, 32);
            _edit.Enabled = false;

            _add.Click += (_, __) => AddFilesDialog();
            _edit.Click += (_, __) => EditSelectedEntry();
            _clear.Click += (_, __) =>
            {
                _entries.Clear();
                _list.Items.Clear();
                _emptyHint.Visible = true;
                _edit.Enabled = false;
                SetStatus("");
            };
            toolbar.Controls.AddRange(new Control[] { _add, _edit, _clear });

            if (SessionContext.CanManage)
            {
                Btn(_manageLeaders, "  Quản lý Email", C_BtnGray, C_GrayFg, 148, 32);
                _manageLeaders.Margin = new Padding(3);
                _manageLeaders.FlatAppearance.BorderSize = 1;
                _manageLeaders.FlatAppearance.BorderColor = C_Border;
                _manageLeaders.Click += (_, __) =>
                {
                    using (var dlg = new Forms.LeaderManagementForm())
                        dlg.ShowDialog(this);
                };
                toolbar.Controls.Add(_manageLeaders);
            }

            return toolbar;
        }

        private Panel BuildListPanel()
        {
            var listPanel = new Panel { Dock = DockStyle.Fill };
            _list.Dock = DockStyle.Fill;
            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.BorderStyle = BorderStyle.None;
            _list.Font = new Font("Segoe UI", 9.5f);
            _list.BackColor = Color.White;
            _list.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            _list.Columns.Add("File .dyn", 255);
            _list.Columns.Add("Tên mục", 120);
            _list.Columns.Add("Tên plugin", 190);
            _list.Columns.Add("Icon", 170);
            _list.Columns.Add("Trạng thái", 210);
            _list.SelectedIndexChanged += (_, __) => _edit.Enabled = _list.SelectedItems.Count == 1;
            _list.DoubleClick += (_, __) => EditSelectedEntry();
            _list.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Enter && _list.SelectedItems.Count == 1)
                {
                    EditSelectedEntry();
                    e.Handled = true;
                }
            };

            _emptyHint.Text = "Kéo & thả file .dyn vào đây\r\nhoặc bấm \"Thêm file .dyn\"";
            _emptyHint.Font = new Font("Segoe UI", 11f);
            _emptyHint.ForeColor = Color.FromArgb(168, 184, 212);
            _emptyHint.TextAlign = ContentAlignment.MiddleCenter;
            _emptyHint.Dock = DockStyle.Fill;
            _emptyHint.BackColor = Color.White;
            _emptyHint.Visible = true;

            listPanel.Controls.Add(_list);
            listPanel.Controls.Add(_emptyHint);
            _emptyHint.BringToFront();
            return listPanel;
        }

        private FlowLayoutPanel BuildBottomBar()
        {
            var bottomFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                Height = 58,
                Padding = new Padding(14, 10, 14, 10),
                BackColor = C_Toolbar,
            };

            Btn(_run, "  Mã hóa tất cả", C_BtnGreen, Color.White, 170, 38);
            Btn(_openFolder, "Mở thư mục kết quả", C_BtnGray, C_GrayFg, 162, 38);
            _openFolder.Enabled = false;
            _run.Click += (_, __) => EncryptAll();
            _openFolder.Click += (_, __) => OpenLastFolder();
            bottomFlow.Controls.AddRange(new Control[] { _run, _openFolder });
            return bottomFlow;
        }

        static void Btn(Button b, string text, Color bg, Color fg, int w, int h)
        {
            b.Text = text;
            b.Width = w;
            b.Height = h;
            b.BackColor = bg;
            b.ForeColor = fg;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.BorderColor = bg;
            b.Font = new Font("Segoe UI", 9.5f);
            b.Cursor = Cursors.Hand;
        }

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effect = DragDropEffects.Copy;
        }

        private void OnDragDrop(object sender, DragEventArgs e)
        {
            AddFiles((string[])e.Data.GetData(DataFormats.FileDrop));
        }

        private void AddFilesDialog()
        {
            using (var dlg = new OpenFileDialog
            {
                Filter = "Dynamo script (*.dyn)|*.dyn",
                Multiselect = true,
                Title = "Chọn file .dyn cần mã hóa",
            })
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                    AddFiles(dlg.FileNames);
            }
        }

        private void AddFiles(IEnumerable<string> paths)
        {
            foreach (var f in ResolveDynFiles(paths))
            {
                if (_entries.Any(x => string.Equals(x.FilePath, f, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var entry = new DynFileEntry
                {
                    FilePath = f,
                    PanelName = "Utilities",
                    PluginName = Path.GetFileNameWithoutExtension(f),
                    IconPath = "",
                    Status = "Chờ nhập tên/icon",
                };

                using (var dlg = new DynFileMetadataDialog(entry, false))
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK)
                        continue;
                }

                entry.Status = "Chờ mã hóa";
                _entries.Add(entry);
                _list.Items.Add(CreateListItem(entry));
            }

            _emptyHint.Visible = _entries.Count == 0;
            SetStatus(_entries.Count + " file trong danh sách. Double-click vào một dòng để sửa tên/icon.");
        }

        private static IEnumerable<string> ResolveDynFiles(IEnumerable<string> paths)
        {
            foreach (var p in paths)
            {
                if (Directory.Exists(p))
                {
                    foreach (var f in Directory.GetFiles(p, "*.dyn"))
                        yield return f;
                }
                else if (File.Exists(p) &&
                         string.Equals(Path.GetExtension(p), ".dyn", StringComparison.OrdinalIgnoreCase))
                {
                    yield return p;
                }
            }
        }

        private ListViewItem CreateListItem(DynFileEntry entry)
        {
            var item = new ListViewItem(Path.GetFileName(entry.FilePath));
            item.Tag = entry;
            item.SubItems.Add(entry.PanelName);
            item.SubItems.Add(entry.PluginName);
            item.SubItems.Add(Path.GetFileName(entry.IconPath));
            item.SubItems.Add(entry.Status);
            return item;
        }

        private static void RefreshListItem(ListViewItem item)
        {
            var entry = (DynFileEntry)item.Tag;
            item.Text = Path.GetFileName(entry.FilePath);
            item.SubItems[1].Text = entry.PanelName;
            item.SubItems[2].Text = entry.PluginName;
            item.SubItems[3].Text = Path.GetFileName(entry.IconPath);
            item.SubItems[4].Text = entry.Status;
        }

        private void EditSelectedEntry()
        {
            if (_list.SelectedItems.Count != 1)
                return;

            var item = _list.SelectedItems[0];
            var entry = (DynFileEntry)item.Tag;
            using (var dlg = new DynFileMetadataDialog(entry, true))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    entry.Status = "Chờ mã hóa";
                    item.ForeColor = Color.Black;
                    RefreshListItem(item);
                    SetStatus("Đã cập nhật tên/icon cho " + Path.GetFileName(entry.FilePath) + ".");
                }
            }
        }

        private void EncryptAll()
        {
            if (_entries.Count == 0)
            {
                MessageBox.Show(this, "Chưa có file nào. Hãy thêm file .dyn trước.",
                    "BIMLab", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var invalid = _entries.FirstOrDefault(x => !x.IsReady);
            if (invalid != null)
            {
                MessageBox.Show(this,
                    "File này chưa đủ tên/icon:\n" + Path.GetFileName(invalid.FilePath) +
                    "\n\nHãy double-click vào dòng file để nhập lại.",
                    "BIMLab", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                SelectEntry(invalid);
                return;
            }

            if (!Secrets.TryGetMasterKeyBytes(out byte[] masterKey, out string keyError))
            {
                MessageBox.Show(this, keyError,
                    "BIMLab", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            int ok = 0, fail = 0;
            for (int i = 0; i < _entries.Count; i++)
            {
                var entry = _entries[i];
                try
                {
                    string graphJson = File.ReadAllText(entry.FilePath);
                    byte[] plain = DynxPackage.Create(
                        graphJson,
                        entry.PanelName,
                        entry.PluginName,
                        entry.IconPath,
                        Path.GetFileName(entry.FilePath));
                    byte[] blob = DynxCrypto.Encrypt(plain, masterKey);
                    string outPath = Path.Combine(
                        Path.GetDirectoryName(entry.FilePath),
                        Path.GetFileNameWithoutExtension(entry.FilePath) + ".dynx");
                    File.WriteAllBytes(outPath, blob);
                    _lastOutDir = Path.GetDirectoryName(outPath);

                    entry.Status = "OK Xong -> " + Path.GetFileName(outPath);
                    RefreshListItem(_list.Items[i]);
                    _list.Items[i].ForeColor = Color.FromArgb(30, 120, 68);
                    ok++;
                }
                catch (Exception ex)
                {
                    entry.Status = "Lỗi: " + ex.Message;
                    RefreshListItem(_list.Items[i]);
                    _list.Items[i].ForeColor = Color.FromArgb(192, 40, 40);
                    fail++;
                }
            }

            _openFolder.Enabled = _lastOutDir != null;
            SetStatus($"Hoàn tất: {ok} thành công" + (fail > 0 ? $", {fail} lỗi." : "."));
            MessageBox.Show(this,
                $"Đã mã hóa {ok} file" + (fail > 0 ? $", {fail} file lỗi." : ".") +
                "\n\nChỉ gửi file .dynx cho member. Giữ file .dyn gốc lại.",
                "BIMLab", MessageBoxButtons.OK,
                fail > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
        }

        private void SelectEntry(DynFileEntry entry)
        {
            foreach (ListViewItem item in _list.Items)
            {
                if (ReferenceEquals(item.Tag, entry))
                {
                    item.Selected = true;
                    item.EnsureVisible();
                    _list.Focus();
                    break;
                }
            }
        }

        private void OpenLastFolder()
        {
            if (_lastOutDir != null && Directory.Exists(_lastOutDir))
                System.Diagnostics.Process.Start("explorer.exe", "\"" + _lastOutDir + "\"");
        }

        private void SetStatus(string text) => _status.Text = text;

        private static Bitmap LoadBimDynamoIcon(int size)
        {
            try
            {
                var asm = System.Reflection.Assembly.GetExecutingAssembly();
                using (var s = asm.GetManifestResourceStream("DynLock.EncryptorGui.Resources.BimDynamo.ico"))
                {
                    if (s == null) return null;
                    var raw = new byte[s.Length];
                    s.Read(raw, 0, raw.Length);

                    int count = BitConverter.ToUInt16(raw, 4);
                    int bestIdx = 0, bestDiff = int.MaxValue;
                    for (int i = 0; i < count; i++)
                    {
                        int b = 6 + i * 16;
                        int w = raw[b] == 0 ? 256 : raw[b];
                        int d = Math.Abs(w - size);
                        if (d < bestDiff) { bestDiff = d; bestIdx = i; }
                    }

                    int entryBase = 6 + bestIdx * 16;
                    int dataSize = (int)BitConverter.ToUInt32(raw, entryBase + 8);
                    int dataOff = (int)BitConverter.ToUInt32(raw, entryBase + 12);
                    using (var ms = new MemoryStream(raw, dataOff, dataSize))
                    {
                        var src = new Bitmap(ms);
                        if (src.Width == size && src.Height == size) return src;

                        var resized = new Bitmap(size, size);
                        using (var g = Graphics.FromImage(resized))
                        {
                            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                            g.DrawImage(src, 0, 0, size, size);
                        }
                        src.Dispose();
                        return resized;
                    }
                }
            }
            catch { return null; }
        }

        sealed class DynFileEntry
        {
            public string FilePath { get; set; }
            public string PanelName { get; set; }
            public string PluginName { get; set; }
            public string IconPath { get; set; }
            public string Status { get; set; }

            public bool IsReady =>
                !string.IsNullOrWhiteSpace(PanelName) &&
                !string.IsNullOrWhiteSpace(PluginName) &&
                !string.IsNullOrWhiteSpace(IconPath) &&
                File.Exists(IconPath);
        }

        sealed class DynFileMetadataDialog : Form
        {
            readonly DynFileEntry _entry;
            readonly TextBox _filePath = new TextBox();
            readonly TextBox _panelName = new TextBox();
            readonly TextBox _pluginName = new TextBox();
            readonly TextBox _iconPath = new TextBox();
            readonly Button _browseIcon = new Button();
            readonly Button _ok = new Button();
            readonly Button _cancel = new Button();

            public DynFileMetadataDialog(DynFileEntry entry, bool editMode)
            {
                _entry = entry;
                Text = editMode ? "Sửa tên/icon cho file .dyn" : "Nhập tên/icon cho file .dyn";
                Icon = AppChrome.Icon;
                Size = new Size(620, 260);
                MinimumSize = new Size(560, 240);
                StartPosition = FormStartPosition.CenterParent;
                Font = new Font("Segoe UI", 9.5f);
                BackColor = Color.White;
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MaximizeBox = false;
                MinimizeBox = false;

                var table = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    Padding = new Padding(16),
                    ColumnCount = 3,
                    RowCount = 5,
                };
                table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
                table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
                table.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
                table.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
                table.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
                table.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
                table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

                _filePath.ReadOnly = true;
                _filePath.Text = entry.FilePath;
                _panelName.Text = string.IsNullOrWhiteSpace(entry.PanelName) ? "Utilities" : entry.PanelName;
                _pluginName.Text = string.IsNullOrWhiteSpace(entry.PluginName)
                    ? Path.GetFileNameWithoutExtension(entry.FilePath)
                    : entry.PluginName;
                _iconPath.Text = entry.IconPath ?? "";
                _iconPath.ReadOnly = true;

                AddLabel(table, "File .dyn", 0);
                table.Controls.Add(_filePath, 1, 0);
                table.SetColumnSpan(_filePath, 2);
                AddLabel(table, "Tên mục", 1);
                table.Controls.Add(_panelName, 1, 1);
                table.SetColumnSpan(_panelName, 2);
                AddLabel(table, "Tên plugin", 2);
                table.Controls.Add(_pluginName, 1, 2);
                table.SetColumnSpan(_pluginName, 2);
                AddLabel(table, "Icon", 3);
                table.Controls.Add(_iconPath, 1, 3);
                Btn(_browseIcon, "Chọn icon", C_BtnGray, C_GrayFg, 96, 28);
                _browseIcon.Click += (_, __) => ChooseIcon();
                table.Controls.Add(_browseIcon, 2, 3);

                var buttons = new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    FlowDirection = FlowDirection.RightToLeft,
                    Padding = new Padding(0, 14, 0, 0),
                };
                Btn(_ok, "Lưu", C_BtnBlue, Color.White, 92, 32);
                Btn(_cancel, "Hủy", C_BtnGray, C_GrayFg, 92, 32);
                _ok.Click += (_, __) => Save();
                _cancel.Click += (_, __) => DialogResult = DialogResult.Cancel;
                buttons.Controls.AddRange(new Control[] { _ok, _cancel });
                table.Controls.Add(buttons, 0, 4);
                table.SetColumnSpan(buttons, 3);

                foreach (Control c in table.Controls)
                {
                    c.Margin = new Padding(3, 3, 8, 3);
                    if (c is TextBox tb)
                        tb.Dock = DockStyle.Fill;
                }

                AcceptButton = _ok;
                CancelButton = _cancel;
                Controls.Add(table);
            }

            private static void AddLabel(TableLayoutPanel table, string text, int row)
            {
                table.Controls.Add(new Label
                {
                    Text = text,
                    TextAlign = ContentAlignment.MiddleLeft,
                    ForeColor = C_GrayFg,
                    Dock = DockStyle.Fill,
                }, 0, row);
            }

            private void ChooseIcon()
            {
                using (var dlg = new OpenFileDialog
                {
                    Filter = "Image files (*.png;*.jpg;*.jpeg;*.bmp;*.ico)|*.png;*.jpg;*.jpeg;*.bmp;*.ico",
                    Title = "Chọn icon cho file .dyn",
                })
                {
                    if (dlg.ShowDialog(this) == DialogResult.OK)
                        _iconPath.Text = dlg.FileName;
                }
            }

            private void Save()
            {
                if (string.IsNullOrWhiteSpace(_panelName.Text))
                {
                    MessageBox.Show(this, "Vui lòng nhập tên mục.", "BIMLab",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    _panelName.Focus();
                    return;
                }

                if (string.IsNullOrWhiteSpace(_pluginName.Text))
                {
                    MessageBox.Show(this, "Vui lòng nhập tên plugin.", "BIMLab",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    _pluginName.Focus();
                    return;
                }

                if (string.IsNullOrWhiteSpace(_iconPath.Text) || !File.Exists(_iconPath.Text))
                {
                    MessageBox.Show(this, "Vui lòng chọn icon cho file .dyn này.", "BIMLab",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    _browseIcon.Focus();
                    return;
                }

                _entry.PanelName = _panelName.Text.Trim();
                _entry.PluginName = _pluginName.Text.Trim();
                _entry.IconPath = _iconPath.Text.Trim();
                DialogResult = DialogResult.OK;
            }
        }
    }
}
