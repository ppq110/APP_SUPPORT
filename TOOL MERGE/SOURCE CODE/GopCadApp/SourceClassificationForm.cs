namespace GopCadApp;

internal sealed class SourceClassificationForm : Form
{
    private const string DirectMergeText = "Loại 1 - Gộp trực tiếp";
    private const string ExportLayoutText = "Loại 2 - Export layout trước";

    private readonly DataGridView _grid = new()
    {
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false,
        Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right | AnchorStyles.Bottom
    };

    private readonly List<DwgSourceItem> _items;

    public IReadOnlyList<DwgSourceItem> Items => _items;

    public SourceClassificationForm(IEnumerable<DwgSourceItem> items)
    {
        _items = items.ToList();

        Text = "Phân loại DWG";
        Width = 780;
        Height = 500;
        MinimumSize = new Size(640, 380);
        StartPosition = FormStartPosition.CenterParent;

        Label note = new()
        {
            Text = "Chọn loại cho từng file DWG trước khi gộp.",
            Left = 14,
            Top = 14,
            AutoSize = true
        };

        Label hint = new()
        {
            Text = "Loại 1: model đã export từ layout. Loại 2: model gốc + layout gốc, cần export layout trước.",
            Left = 14,
            Top = 38,
            AutoSize = true
        };

        BuildGrid();
        _grid.Left = 14;
        _grid.Top = 68;
        _grid.Width = ClientSize.Width - 28;
        _grid.Height = ClientSize.Height - 126;

        Button directAllButton = new() { Text = "Tất cả Loại 1", Width = 110, Height = 30, Left = 14, Anchor = AnchorStyles.Left | AnchorStyles.Bottom };
        directAllButton.Top = ClientSize.Height - 44;
        directAllButton.Click += (_, _) => SetAllKinds(DirectMergeText);

        Button exportAllButton = new() { Text = "Tất cả Loại 2", Width = 110, Height = 30, Left = 132, Anchor = AnchorStyles.Left | AnchorStyles.Bottom };
        exportAllButton.Top = ClientSize.Height - 44;
        exportAllButton.Click += (_, _) => SetAllKinds(ExportLayoutText);

        Button okButton = new() { Text = "OK", Width = 90, Height = 30, Anchor = AnchorStyles.Right | AnchorStyles.Bottom };
        okButton.Left = ClientSize.Width - 198;
        okButton.Top = ClientSize.Height - 44;
        okButton.Click += (_, _) =>
        {
            SaveGrid();
            DialogResult = DialogResult.OK;
            Close();
        };

        Button cancelButton = new() { Text = "Hủy", Width = 90, Height = 30, Anchor = AnchorStyles.Right | AnchorStyles.Bottom };
        cancelButton.Left = ClientSize.Width - 104;
        cancelButton.Top = ClientSize.Height - 44;
        cancelButton.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };

        Controls.AddRange([note, hint, _grid, directAllButton, exportAllButton, okButton, cancelButton]);

        Resize += (_, _) =>
        {
            _grid.Width = ClientSize.Width - 28;
            _grid.Height = ClientSize.Height - 126;
            directAllButton.Top = ClientSize.Height - 44;
            exportAllButton.Top = ClientSize.Height - 44;
            okButton.Left = ClientSize.Width - 198;
            okButton.Top = ClientSize.Height - 44;
            cancelButton.Left = ClientSize.Width - 104;
            cancelButton.Top = ClientSize.Height - 44;
        };
    }

    private void BuildGrid()
    {
        DataGridViewTextBoxColumn nameColumn = new()
        {
            HeaderText = "File DWG",
            ReadOnly = true,
            FillWeight = 58
        };

        DataGridViewComboBoxColumn kindColumn = new()
        {
            HeaderText = "Loại xử lý",
            FillWeight = 42,
            FlatStyle = FlatStyle.Flat
        };
        kindColumn.Items.Add(DirectMergeText);
        kindColumn.Items.Add(ExportLayoutText);

        _grid.Columns.Add(nameColumn);
        _grid.Columns.Add(kindColumn);

        foreach (DwgSourceItem item in _items)
        {
            int rowIndex = _grid.Rows.Add(
                Path.GetFileName(item.Path),
                item.Kind == DwgSourceKind.ExportLayoutFirst ? ExportLayoutText : DirectMergeText);
            _grid.Rows[rowIndex].Tag = item.Path;
        }
    }

    private void SetAllKinds(string kindText)
    {
        foreach (DataGridViewRow row in _grid.Rows)
        {
            row.Cells[1].Value = kindText;
        }
    }

    private void SaveGrid()
    {
        _items.Clear();
        foreach (DataGridViewRow row in _grid.Rows)
        {
            string? path = row.Tag as string;
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            string kindText = Convert.ToString(row.Cells[1].Value) ?? DirectMergeText;
            DwgSourceKind kind = kindText == ExportLayoutText
                ? DwgSourceKind.ExportLayoutFirst
                : DwgSourceKind.DirectMerge;
            _items.Add(new DwgSourceItem(path, kind));
        }
    }
}
