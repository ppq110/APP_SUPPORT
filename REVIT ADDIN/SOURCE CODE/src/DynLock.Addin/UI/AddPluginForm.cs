using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using DynLock.Addin.DynamicPlugins;

namespace DynLock.Addin.UI
{
    public class AddPluginForm : Form
    {
        private readonly TextBox _dynxPath = new TextBox();
        private readonly TextBox _panelName = new TextBox();
        private readonly TextBox _buttonName = new TextBox();
        private readonly Button _ok = new Button();

        public AddPluginForm()
        {
            Text = "BIMLab Bách Khoa - Thêm plugin";
            Width = 560;
            Height = 300;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = Color.White;
            Font = new Font("Segoe UI", 9.5f);

            var header = new Panel { Dock = DockStyle.Top, Height = 70, BackColor = Color.FromArgb(18, 90, 175) };
            header.Controls.Add(new Label
            {
                Text = "Thêm plugin .dynx",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 12.5f, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(18, 14),
            });
            header.Controls.Add(new Label
            {
                Text = "Chọn file .dynx, đặt panel và tên nút sẽ hiện trên ribbon BIMLab",
                ForeColor = Color.FromArgb(160, 200, 255),
                Font = new Font("Segoe UI", 8.5f),
                AutoSize = true,
                Location = new Point(20, 42),
            });

            var body = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(18, 14, 18, 4),
                ColumnCount = 3,
                RowCount = 3,
                BackColor = Color.White,
            };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 94));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));

            var browse = new Button { Text = "Chọn..." };
            StyleButton(browse, Color.FromArgb(218, 226, 242), Color.FromArgb(52, 72, 108), 78, 28);
            browse.Click += (_, __) => BrowseDynx();

            AddLabel(body, "File .dynx", 0);
            AddLabel(body, "Panel", 1);
            AddLabel(body, "Tên plugin", 2);

            _dynxPath.ReadOnly = true;
            _panelName.Text = "Utilities";
            StyleTextBox(_dynxPath);
            StyleTextBox(_panelName);
            StyleTextBox(_buttonName);

            body.Controls.Add(_dynxPath, 1, 0);
            body.Controls.Add(browse, 2, 0);
            body.Controls.Add(_panelName, 1, 1);
            body.SetColumnSpan(_panelName, 2);
            body.Controls.Add(_buttonName, 1, 2);
            body.SetColumnSpan(_buttonName, 2);

            var bottom = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 58,
                Padding = new Padding(12, 9, 12, 9),
                FlowDirection = FlowDirection.RightToLeft,
                BackColor = Color.FromArgb(244, 246, 251),
            };

            var cancel = new Button { Text = "Hủy", DialogResult = DialogResult.Cancel };
            _ok.Text = "Thêm";
            StyleButton(cancel, Color.FromArgb(218, 226, 242), Color.FromArgb(52, 72, 108), 88, 36);
            StyleButton(_ok, Color.FromArgb(34, 126, 68), Color.White, 96, 36);
            _ok.Click += (_, __) => SavePlugin();
            bottom.Controls.Add(_ok);
            bottom.Controls.Add(cancel);

            Controls.Add(body);
            Controls.Add(header);
            Controls.Add(bottom);

            AcceptButton = _ok;
            CancelButton = cancel;
        }

        private static void AddLabel(TableLayoutPanel table, string text, int row)
        {
            table.Controls.Add(new Label
            {
                Text = text,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.FromArgb(52, 72, 108),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            }, 0, row);
        }

        private static void StyleTextBox(TextBox textBox)
        {
            textBox.Dock = DockStyle.Fill;
            textBox.BorderStyle = BorderStyle.FixedSingle;
            textBox.Font = new Font("Segoe UI", 9.5f);
            textBox.Margin = new Padding(3, 4, 8, 4);
        }

        private static void StyleButton(Button b, Color bg, Color fg, int w, int h)
        {
            b.Width = w;
            b.Height = h;
            b.BackColor = bg;
            b.ForeColor = fg;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.Font = new Font("Segoe UI", 9.5f);
            b.Cursor = Cursors.Hand;
        }

        private void BrowseDynx()
        {
            using (var dlg = new OpenFileDialog
            {
                Filter = "BIMLab dynx (*.dynx)|*.dynx",
                Title = "Chọn file .dynx",
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK)
                    return;

                _dynxPath.Text = dlg.FileName;
                if (string.IsNullOrWhiteSpace(_buttonName.Text))
                    _buttonName.Text = Path.GetFileNameWithoutExtension(dlg.FileName);
            }
        }

        private void SavePlugin()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_dynxPath.Text) || !File.Exists(_dynxPath.Text))
                {
                    MessageBox.Show(this, "Vui lòng chọn file .dynx hợp lệ.", "BIMLab Bách Khoa",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (string.IsNullOrWhiteSpace(_panelName.Text) || string.IsNullOrWhiteSpace(_buttonName.Text))
                {
                    MessageBox.Show(this, "Vui lòng nhập đầy đủ Panel và Tên plugin.", "BIMLab Bách Khoa",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (PluginConfigStore.OrderedPlugins().Count >= PluginConfigStore.MaxRibbonPlugins)
                {
                    MessageBox.Show(this, "BIMLab hiện hỗ trợ tối đa 30 plugin trên ribbon.", "BIMLab Bách Khoa",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                PluginConfigStore.AddPlugin(_dynxPath.Text, _panelName.Text, _buttonName.Text);
                MessageBox.Show(this,
                    "Đã thêm plugin.\n\nNếu nút chưa hiện trên ribbon, hãy khởi động lại Revit.",
                    "BIMLab Bách Khoa",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "BIMLab Bách Khoa",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
