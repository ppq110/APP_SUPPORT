using System;
using System.Drawing;
using System.Windows.Forms;
using DynLock.Addin.DynamicPlugins;

namespace DynLock.Addin.UI
{
    internal sealed class LoadedPluginPreviewForm : Form
    {
        private readonly PluginItem _plugin;

        public LoadedPluginPreviewForm(PluginItem plugin)
        {
            _plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));

            Text = "BIMLab Bách Khoa - Plugin đã nạp";
            Width = 520;
            Height = 280;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = Color.White;
            Font = new Font("Segoe UI", 9.5f);

            var header = new Panel { Dock = DockStyle.Top, Height = 72, BackColor = Color.FromArgb(18, 90, 175) };
            var iconWrap = new Panel { Bounds = new Rectangle(16, 14, 44, 44), BackColor = Color.White };
            var iconBox = new PictureBox
            {
                Image = AddinIcons.PluginIcon(_plugin.IconPath, 36, _plugin.ButtonName),
                SizeMode = PictureBoxSizeMode.Zoom,
                Bounds = new Rectangle(4, 4, 36, 36),
                BackColor = Color.White,
            };
            iconWrap.Controls.Add(iconBox);

            var lblTitle = new Label
            {
                Text = "Plugin đã được nạp",
                Font = new Font("Segoe UI", 13f, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(72, 14),
            };
            var lblSub = new Label
            {
                Text = "Plugin đã sẵn sàng trên ribbon BIMLab",
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Color.FromArgb(160, 200, 255),
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(74, 42),
            };
            header.Controls.AddRange(new Control[] { iconWrap, lblTitle, lblSub });

            var body = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2,
                Padding = new Padding(18, 16, 18, 8),
                BackColor = Color.White,
            };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

            AddRow(body, 0, "Panel", _plugin.PanelName);
            AddRow(body, 1, "Plugin", _plugin.ButtonName);

            var bottom = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 58,
                Padding = new Padding(12, 8, 12, 8),
                FlowDirection = FlowDirection.RightToLeft,
                BackColor = Color.FromArgb(244, 246, 251),
            };

            var close = new Button { Text = "Đóng", DialogResult = DialogResult.OK };
            StyleButton(close, Color.FromArgb(34, 126, 68), Color.White, 88, 36);
            bottom.Controls.Add(close);

            Controls.Add(body);
            Controls.Add(header);
            Controls.Add(bottom);

            AcceptButton = close;
            CancelButton = close;
        }

        private static void AddRow(TableLayoutPanel table, int row, string label, string value)
        {
            var left = new Label
            {
                Text = label,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.FromArgb(52, 72, 108),
                Font = new Font("Segoe UI", 9.25f, FontStyle.Bold),
            };

            var right = new Label
            {
                Text = string.IsNullOrWhiteSpace(value) ? "(chưa đặt)" : value,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.FromArgb(28, 38, 58),
                BackColor = Color.FromArgb(248, 250, 254),
                BorderStyle = BorderStyle.FixedSingle,
                Padding = new Padding(8, 0, 8, 0),
                AutoEllipsis = true,
            };

            table.Controls.Add(left, 0, row);
            table.Controls.Add(right, 1, row);
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
    }
}
