namespace GopCadApp;

internal sealed class SourceClassificationForm : Form
{
    private const string DirectMergeText = "Loại 1 - Gộp trực tiếp";
    private const string ExportLayoutText = "Loại 2 - Export layout trước";

    private const int IncludeColumn = 0;
    private const int NameColumn = 1;
    private const int KindColumn = 2;

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

    private readonly Label _countLabel = new()
    {
        AutoSize = true,
        Top = 14,
        Anchor = AnchorStyles.Top | AnchorStyles.Right
    };

    private readonly List<DwgSourceItem> _items;

    // Chỉ gồm các file còn được tick "Gộp" khi bấm OK.
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
            Text = "Tick những file cần gộp và chọn loại xử lý cho từng file.",
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
        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            // Tick/bỏ tick có hiệu lực ngay (không đợi rời ô) để bộ đếm và nút OK luôn đúng.
            if (_grid.IsCurrentCellDirty)
            {
                _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        };
        _grid.CellValueChanged += (_, _) => UpdateCount();

        _countLabel.Left = ClientSize.Width - 200;

        // 4 nút bên trái nằm gọn trong 14..418 để không đè lên nút OK ở bề rộng tối thiểu (640).
        Button directAllButton = new() { Text = "Tất cả Loại 1", Width = 108, Height = 30, Left = 14, Anchor = AnchorStyles.Left | AnchorStyles.Bottom };
        directAllButton.Top = ClientSize.Height - 44;
        directAllButton.Click += (_, _) => SetAllKinds(DirectMergeText);

        Button exportAllButton = new() { Text = "Tất cả Loại 2", Width = 108, Height = 30, Left = 126, Anchor = AnchorStyles.Left | AnchorStyles.Bottom };
        exportAllButton.Top = ClientSize.Height - 44;
        exportAllButton.Click += (_, _) => SetAllKinds(ExportLayoutText);

        Button checkAllButton = new() { Text = "Chọn hết", Width = 80, Height = 30, Left = 238, Anchor = AnchorStyles.Left | AnchorStyles.Bottom };
        checkAllButton.Top = ClientSize.Height - 44;
        checkAllButton.Click += (_, _) => SetAllIncluded(true);

        Button uncheckAllButton = new() { Text = "Bỏ chọn hết", Width = 96, Height = 30, Left = 322, Anchor = AnchorStyles.Left | AnchorStyles.Bottom };
        uncheckAllButton.Top = ClientSize.Height - 44;
        uncheckAllButton.Click += (_, _) => SetAllIncluded(false);

        Button okButton = new() { Text = "OK", Width = 90, Height = 30, Anchor = AnchorStyles.Right | AnchorStyles.Bottom };
        okButton.Left = ClientSize.Width - 198;
        okButton.Top = ClientSize.Height - 44;
        okButton.Click += (_, _) =>
        {
            SaveGrid();
            if (_items.Count == 0)
            {
                MessageBox.Show(this, "Chưa tick file nào để gộp.", "BIMLab DWG Merge", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

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

        Controls.AddRange([note, hint, _countLabel, _grid, directAllButton, exportAllButton, checkAllButton, uncheckAllButton, okButton, cancelButton]);

        Resize += (_, _) =>
        {
            _grid.Width = ClientSize.Width - 28;
            _grid.Height = ClientSize.Height - 126;
            _countLabel.Left = ClientSize.Width - _countLabel.Width - 14;
            directAllButton.Top = ClientSize.Height - 44;
            exportAllButton.Top = ClientSize.Height - 44;
            checkAllButton.Top = ClientSize.Height - 44;
            uncheckAllButton.Top = ClientSize.Height - 44;
            okButton.Left = ClientSize.Width - 198;
            okButton.Top = ClientSize.Height - 44;
            cancelButton.Left = ClientSize.Width - 104;
            cancelButton.Top = ClientSize.Height - 44;
        };

        UpdateCount();
    }

    private void BuildGrid()
    {
        DataGridViewCheckBoxColumn includeColumn = new()
        {
            HeaderText = "Gộp",
            FillWeight = 10
        };

        DataGridViewTextBoxColumn nameColumn = new()
        {
            HeaderText = "File DWG",
            ReadOnly = true,
            FillWeight = 52
        };

        DataGridViewComboBoxColumn kindColumn = new()
        {
            HeaderText = "Loại xử lý",
            FillWeight = 38,
            FlatStyle = FlatStyle.Flat
        };
        kindColumn.Items.Add(DirectMergeText);
        kindColumn.Items.Add(ExportLayoutText);

        _grid.Columns.Add(includeColumn);
        _grid.Columns.Add(nameColumn);
        _grid.Columns.Add(kindColumn);

        foreach (DwgSourceItem item in _items)
        {
            int rowIndex = _grid.Rows.Add(
                true,
                Path.GetFileName(item.Path),
                item.Kind == DwgSourceKind.ExportLayoutFirst ? ExportLayoutText : DirectMergeText);
            _grid.Rows[rowIndex].Tag = item.Path;
            // Rê chuột vào tên file để xem đường dẫn đầy đủ (hữu ích khi chọn cả thư mục con).
            _grid.Rows[rowIndex].Cells[NameColumn].ToolTipText = item.Path;
        }
    }

    private void SetAllKinds(string kindText)
    {
        foreach (DataGridViewRow row in _grid.Rows)
        {
            row.Cells[KindColumn].Value = kindText;
        }
    }

    private void SetAllIncluded(bool included)
    {
        foreach (DataGridViewRow row in _grid.Rows)
        {
            row.Cells[IncludeColumn].Value = included;
        }
    }

    private static bool IsIncluded(DataGridViewRow row)
    {
        return row.Cells[IncludeColumn].Value is true;
    }

    private void UpdateCount()
    {
        int included = _grid.Rows.Cast<DataGridViewRow>().Count(IsIncluded);
        _countLabel.Text = $"Đã chọn {included}/{_grid.Rows.Count} file";
        _countLabel.Left = ClientSize.Width - _countLabel.Width - 14;
    }

    private void SaveGrid()
    {
        _items.Clear();
        foreach (DataGridViewRow row in _grid.Rows)
        {
            string? path = row.Tag as string;
            if (string.IsNullOrWhiteSpace(path) || !IsIncluded(row))
            {
                continue;
            }

            string kindText = Convert.ToString(row.Cells[KindColumn].Value, System.Globalization.CultureInfo.InvariantCulture) ?? DirectMergeText;
            DwgSourceKind kind = kindText == ExportLayoutText
                ? DwgSourceKind.ExportLayoutFirst
                : DwgSourceKind.DirectMerge;
            _items.Add(new DwgSourceItem(path, kind));
        }
    }
}
