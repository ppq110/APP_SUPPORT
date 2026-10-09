import React, { useEffect, useMemo, useRef, useState } from 'react';
import { createRoot } from 'react-dom/client';
import { Download, FileJson, FolderOpen, Palette, RotateCcw, Upload } from 'lucide-react';
import './styles.css';
import headerLogo from './assets/header-logo.png';

const FILE_NAME_COLUMN = 'Tên file';
const DEFAULT_SUMMARY_SHEET_NAME = 'Tong hop khoi luong';
const numberLike = /^-?\d+(?:[.,]\d+)?$/;
const tabs = [
  ['raw', 'Dữ liệu gốc'],
  ['summary', 'Tổng hợp khối lượng'],
  ['pivot', 'Pivot Table khối lượng'],
  ['factor', 'Hệ số nhân'],
  ['mapping', 'Cài đặt Mapping'],
];

const themes = [
  {
    id: 'blue',
    name: 'Xanh dương',
    accent: '#0b66d8',
    accentStrong: '#0b4ea2',
    accentSoft: '#dbeafe',
    hover: '#edf6ff',
    appBg: '#eef2f7',
    headerBg: '#e8eef7',
  },
  {
    id: 'green',
    name: 'Xanh lá',
    accent: '#15803d',
    accentStrong: '#166534',
    accentSoft: '#dcfce7',
    hover: '#f0fdf4',
    appBg: '#eef7f0',
    headerBg: '#e7f3ea',
  },
  {
    id: 'teal',
    name: 'Xanh ngọc',
    accent: '#0f766e',
    accentStrong: '#115e59',
    accentSoft: '#ccfbf1',
    hover: '#f0fdfa',
    appBg: '#edf7f6',
    headerBg: '#e2f1ef',
  },
  {
    id: 'rose',
    name: 'Đỏ hồng',
    accent: '#be123c',
    accentStrong: '#9f1239',
    accentSoft: '#ffe4e6',
    hover: '#fff1f2',
    appBg: '#f8eef1',
    headerBg: '#f5e4e9',
  },
  {
    id: 'dark',
    name: 'Tối',
    accent: '#38bdf8',
    accentStrong: '#0ea5e9',
    accentSoft: '#1e3a5f',
    hover: '#162536',
    appBg: '#0f172a',
    headerBg: '#1e293b',
    surface: '#111c2e',
    text: '#e5eef8',
    muted: '#9fb0c5',
    border: '#334155',
    tableAlt: '#142136',
    rowHover: '#263449',
  },
];

function getInitialThemeId() {
  if (typeof window === 'undefined') return 'blue';
  return localStorage.getItem('txt-convert-theme') || 'blue';
}

function getThemeVars(theme) {
  return {
    '--app-bg': theme.appBg,
    '--surface': theme.surface || '#ffffff',
    '--text': theme.text || '#162033',
    '--muted': theme.muted || '#64748b',
    '--border': theme.border || '#d5dde8',
    '--soft-border': theme.border || '#d6deea',
    '--button-border': theme.border || '#c7d2e1',
    '--tab-bg': theme.surface ? '#162536' : '#f5f7fb',
    '--header-bg': theme.headerBg,
    '--accent': theme.accent,
    '--accent-strong': theme.accentStrong,
    '--accent-soft': theme.accentSoft,
    '--hover': theme.hover,
    '--table-alt': theme.tableAlt || '#fbfdff',
    '--row-hover': theme.rowHover || '#fff7df',
  };
}

function parseTsvLine(line) {
  const cells = [];
  let cell = '';
  let inQuotes = false;
  for (let i = 0; i < line.length; i += 1) {
    const char = line[i];
    const next = line[i + 1];
    if (char === '"' && inQuotes && next === '"') {
      // Escaped quote "" chỉ có nghĩa khi đang TRONG quotes
      cell += '"';
      i += 1;
    } else if (char === '"') {
      inQuotes = !inQuotes;
    } else if (char === '\t' && !inQuotes) {
      cells.push(cell.trim());
      cell = '';
    } else {
      cell += char;
    }
  }
  cells.push(cell.trim());
  return cells;
}

function toNumber(value) {
  const normalized = String(value ?? '').trim().replace(',', '.');
  return numberLike.test(normalized) ? Number(normalized) : NaN;
}

function round(value) {
  return Math.round((Number(value) + Number.EPSILON) * 100) / 100;
}

function formatNumber(value) {
  return new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 2 }).format(Number(value) || 0);
}

function unique(items) {
  return Array.from(new Set(items.filter(Boolean)));
}

function downloadText(name, content, type = 'application/json') {
  const blob = new Blob([content], { type });
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = name;
  link.click();
  URL.revokeObjectURL(url);
}

async function readTextFile(file) {
  const buffer = await file.arrayBuffer();
  let text = new TextDecoder('utf-8').decode(buffer);
  if (text.includes('\uFFFD')) {
    text = new TextDecoder('windows-1258').decode(buffer);
  }
  return text.replace(/^\uFEFF/, '');
}

// Loại 1: dòng 1 là title đơn  → dòng 2 là header, data từ dòng 3
// Loại 2: dòng 1 đã là header  → data từ dòng 2, không có title riêng
//
// Detect: đếm số cột CÓ NỘI DUNG THẬT (không rỗng) trong dòng 1
//   >= 3 cột có nội dung → Loại 2 | < 3 cột → Loại 1
//
// Ví dụ Loại 1: "BT-BE TONG COT-R2" "" "" ""  → 1 cột có nội dung → Loại 1 ✓
// Ví dụ Loại 2: "Hạng mục" "COT2" "Đường kính"... → 12 cột có nội dung → Loại 2 ✓
function isHeaderLine(line) {
  return parseTsvLine(line).filter((cell) => cell.trim() !== '').length >= 3;
}

async function parseFile(file) {
  const text = await readTextFile(file);
  const lines = text.split(/\r?\n/).filter((line) => line.trim());
  if (lines.length < 2) throw new Error('Thiếu header hoặc dữ liệu');

  const hasTitle = !isHeaderLine(lines[0]);
  const title = hasTitle
    ? parseTsvLine(lines[0]).find(Boolean) || file.name.replace(/\.txt$/i, '')
    : file.name.replace(/\.txt$/i, '');
  const headerLine = hasTitle ? lines[1] : lines[0];
  const dataLines  = hasTitle ? lines.slice(2) : lines.slice(1);

  const headers = parseTsvLine(headerLine).map((header, index) => header || `Cột ${index + 1}`);
  const rows = dataLines.map((line) => {
    const cells = parseTsvLine(line);
    const record = { [FILE_NAME_COLUMN]: file.name };
    headers.forEach((header, index) => {
      record[header] = cells[index] ?? '';
    });
    return record;
  });

  return {
    id: `${file.name}-${file.size}-${file.lastModified}`,
    name: file.name,
    path: file.webkitRelativePath || file.name,
    title,
    headers,
    rows,
  };
}

function getNumericColumns(rows, headers) {
  return headers.filter((header) => {
    const filled = rows.map((row) => row[header]).filter((value) => String(value ?? '').trim() !== '');
    if (!filled.length) return false;
    return filled.filter((value) => !Number.isNaN(toNumber(value))).length / filled.length >= 0.75;
  });
}

function getDefaultSummary(headers, numericColumns) {
  const groupColumns = ['COT1', 'COT2'].filter((column) => headers.includes(column));
  const metrics = ['m3', 'm2', 'Volume', 'Material: Area', 'TS_FormworkArea'].filter((column) =>
    numericColumns.includes(column),
  );
  return {
    groupColumns: groupColumns.length ? groupColumns : headers.filter((header) => !numericColumns.includes(header)).slice(0, 2),
    metrics: metrics.length ? metrics.slice(0, 2) : numericColumns.slice(-2),
  };
}

function buildSummary(rows, groupColumns, metrics, factors) {
  if (!rows.length || !groupColumns.length || !metrics.length) return [];
  const map = new Map();
  rows.forEach((row) => {
    const key = groupColumns.map((column) => row[column] || '').join('\u001f');
    if (!map.has(key)) {
      const base = {};
      groupColumns.forEach((column) => {
        base[column] = row[column] || '';
      });
      metrics.forEach((metric) => {
        base[metric] = 0;
      });
      map.set(key, base);
    }
    const target = map.get(key);
    metrics.forEach((metric) => {
      const value = toNumber(row[metric]);
      target[metric] += (Number.isNaN(value) ? 0 : value) * (Number(factors[metric]) || 1);
    });
  });
  return Array.from(map.values()).map((row) => {
    const out = { ...row };
    metrics.forEach((metric) => {
      out[metric] = round(out[metric]);
    });
    return out;
  });
}

function buildPivot(rows, pivotRows, pivotColumns, pivotValues) {
  if (!pivotRows.length || !pivotColumns.length || !pivotValues.length) return { headers: [], rows: [] };
  const valueColumn = pivotValues[0];
  const columns = unique(rows.map((row) => pivotColumns.map((column) => row[column] || '').join(' / '))).sort();
  const axisHeader = `${pivotRows.join(' / ')}\\${pivotColumns.join(' / ')}`;
  const map = new Map();
  rows.forEach((row) => {
    const key = pivotRows.map((column) => row[column] || '').join('\u001f');
    if (!map.has(key)) {
      const base = {};
      pivotRows.forEach((column) => {
        base[column] = row[column] || '';
      });
      columns.forEach((column) => {
        base[column] = 0;
      });
      map.set(key, base);
    }
    const columnKey = pivotColumns.map((column) => row[column] || '').join(' / ');
    const value = toNumber(row[valueColumn]);
    map.get(key)[columnKey] += Number.isNaN(value) ? 0 : value;
  });
  return {
    headers: [axisHeader, ...pivotRows.slice(1), ...columns],
    rows: Array.from(map.values()).map((row) => {
      const out = { ...row };
      out[axisHeader] = row[pivotRows[0]] || '';
      columns.forEach((column) => {
        out[column] = round(out[column]);
      });
      return out;
    }),
  };
}

function aoaFromObjects(rows, headers) {
  return [headers, ...rows.map((row) => headers.map((header) => row[header] ?? ''))];
}

function getExcelColumnWidth(values) {
  const maxLength = values.reduce((max, value) => {
    const text = String(value ?? '');
    return Math.max(max, ...text.split(/\r?\n/).map((line) => line.length));
  }, 0);
  return Math.min(Math.max(maxLength + 2, 10), 42);
}

function formatWorksheet(XLSX, sheet, matrix) {
  const headerStyle = {
    font: { bold: true, color: { rgb: 'FFFFFF' } },
    fill: { patternType: 'solid', fgColor: { rgb: '0B66D8' } },
    alignment: { horizontal: 'center', vertical: 'center', wrapText: true },
    border: {
      top: { style: 'thin', color: { rgb: 'B7C9E4' } },
      bottom: { style: 'thin', color: { rgb: 'B7C9E4' } },
      left: { style: 'thin', color: { rgb: 'B7C9E4' } },
      right: { style: 'thin', color: { rgb: 'B7C9E4' } },
    },
  };
  const bodyStyle = {
    alignment: { vertical: 'center', wrapText: true },
    border: {
      top: { style: 'thin', color: { rgb: 'D9E2EF' } },
      bottom: { style: 'thin', color: { rgb: 'D9E2EF' } },
      left: { style: 'thin', color: { rgb: 'D9E2EF' } },
      right: { style: 'thin', color: { rgb: 'D9E2EF' } },
    },
  };

  sheet['!cols'] = matrix[0].map((_, columnIndex) => {
    const values = matrix.map((row) => row[columnIndex]);
    const isDiagonalHeader = String(matrix[0]?.[columnIndex] ?? '').includes('\\');

    return {
      wch: isDiagonalHeader
        ? Math.max(getExcelColumnWidth(values), 16)
        : getExcelColumnWidth(values),
    };
  });
  sheet['!rows'] = matrix.map((row, rowIndex) => {
    const maxLines = Math.max(1, ...row.map((value) => String(value ?? '').split(/\r?\n/).length));
    const hasDiagonalHeader = rowIndex === 0 && row.some((value) => String(value ?? '').includes('\\'));

    return {
      hpt: rowIndex === 0
        ? hasDiagonalHeader
          ? 48
          : 24
        : Math.max(18, maxLines * 16),
    };
  });
  sheet['!freeze'] = { xSplit: 0, ySplit: 1 };

  matrix.forEach((row, rowIndex) => {
    row.forEach((value, columnIndex) => {
      const address = XLSX.utils.encode_cell({ r: rowIndex, c: columnIndex });
      if (!sheet[address]) return;

      const isDiagonalHeader =
        rowIndex === 0 && String(value ?? '').includes('\\');

      if (isDiagonalHeader) {
        const [bottomText, topText] = String(value ?? '').split('\\');

        sheet[address].v = `${topText}\n\n${bottomText}`;

        sheet[address].s = {
          ...headerStyle,
          alignment: {
            horizontal: 'center',
            vertical: 'center',
            wrapText: true,
          },
          border: {
            ...headerStyle.border,
            diagonal: {
              style: 'thin',
              color: { rgb: 'B7C9E4' },
            },
            diagonalDown: true,
          },
        };

        return;
      }

      sheet[address].s = rowIndex === 0 ? headerStyle : bodyStyle;
    });
  });
}

function getSafeSheetName(workbook, name) {
  const base = String(name || DEFAULT_SUMMARY_SHEET_NAME).replace(/[\\/?*[\]:]/g, ' ').trim().slice(0, 31) || DEFAULT_SUMMARY_SHEET_NAME;
  if (!workbook.SheetNames.includes(base)) return base;

  for (let index = 2; index < 1000; index += 1) {
    const suffix = ` (${index})`;
    const candidate = `${base.slice(0, 31 - suffix.length)}${suffix}`;
    if (!workbook.SheetNames.includes(candidate)) return candidate;
  }

  return `${Date.now()}`.slice(-31);
}

function appendSheet(XLSX, workbook, name, rows, headers) {
  const matrix = aoaFromObjects(rows, headers);
  const sheet = XLSX.utils.aoa_to_sheet(matrix);
  formatWorksheet(XLSX, sheet, matrix);
  XLSX.utils.book_append_sheet(workbook, sheet, getSafeSheetName(workbook, name));
}

function sanitizeSheetName(name) {
  return String(name || '').replace(/[\\/?*[\]:]/g, ' ').trim().slice(0, 31);
}

function getDefaultColumnWidth(header) {
  return Math.min(Math.max(String(header || '').length * 9 + 52, 86), 260);
}

function measureTextWidth(text) {
  const canvas = measureTextWidth.canvas || document.createElement('canvas');
  measureTextWidth.canvas = canvas;
  const context = canvas.getContext('2d');
  context.font = '13px "Segoe UI", Arial, sans-serif';
  return context.measureText(String(text ?? '')).width;
}

function getWrappedLineCount(text, width) {
  const value = String(text ?? '');
  if (!value) return 1;
  return value
    .split(/\r?\n/)
    .reduce((count, line) => count + Math.max(1, Math.ceil((measureTextWidth(line) + 18) / Math.max(width - 18, 32))), 0);
}

function Table({ headers, rows, numericColumns = [], limit = 500, renderCell }) {
  const visible = rows.slice(0, limit);
  const [columnWidths, setColumnWidths] = useState({});
  const [rowHeights, setRowHeights] = useState({});
  const widths = headers.map((header, index) => columnWidths[index] || getDefaultColumnWidth(header));
  const tableWidth = Math.max(920, widths.reduce((total, width) => total + width, 0));

  function startColumnResize(event, index) {
    event.preventDefault();
    event.stopPropagation();
    const startX = event.clientX;
    const startWidth = widths[index];

    function handleMove(moveEvent) {
      const nextWidth = Math.max(54, startWidth + moveEvent.clientX - startX);
      setColumnWidths((current) => ({ ...current, [index]: nextWidth }));
    }

    function handleUp() {
      document.removeEventListener('mousemove', handleMove);
      document.removeEventListener('mouseup', handleUp);
      document.body.classList.remove('isResizingTable');
    }

    document.body.classList.add('isResizingTable');
    document.addEventListener('mousemove', handleMove);
    document.addEventListener('mouseup', handleUp);
  }

  function autoSizeColumn(event, index) {
    event.preventDefault();
    event.stopPropagation();
    const header = headers[index];
    const values = [
      header,
      ...visible.map((row) => (numericColumns.includes(header) ? formatNumber(row[header]) : row[header])),
    ];
    const nextWidth = Math.min(Math.max(...values.map(measureTextWidth)) + 28, 640);
    setColumnWidths((current) => ({ ...current, [index]: Math.max(54, nextWidth) }));
  }

  function startRowResize(event, index) {
    event.preventDefault();
    event.stopPropagation();
    const startY = event.clientY;
    const startHeight = event.currentTarget.closest('tr')?.getBoundingClientRect().height || rowHeights[index] || 40;

    function handleMove(moveEvent) {
      const nextHeight = Math.max(24, startHeight + moveEvent.clientY - startY);
      setRowHeights((current) => ({ ...current, [index]: nextHeight }));
    }

    function handleUp() {
      document.removeEventListener('mousemove', handleMove);
      document.removeEventListener('mouseup', handleUp);
      document.body.classList.remove('isResizingTable');
    }

    document.body.classList.add('isResizingTable');
    document.addEventListener('mousemove', handleMove);
    document.addEventListener('mouseup', handleUp);
  }

  function autoSizeRow(event, index) {
    event.preventDefault();
    event.stopPropagation();
    const row = visible[index] || {};
    const nextHeight = headers.reduce((height, header, columnIndex) => {
      const value = numericColumns.includes(header) ? formatNumber(row[header]) : row[header];
      const lines = getWrappedLineCount(value, widths[columnIndex]);
      return Math.max(height, 16 + lines * 19);
    }, 35);
    setRowHeights((current) => ({ ...current, [index]: nextHeight }));
  }

  return (
    <div className="tableWrap">
      <table className="dataTable" style={{ width: tableWidth, minWidth: tableWidth }}>
        <colgroup>
          {headers.map((header, index) => (
            <col key={`${header}-${index}-col`} style={{ width: widths[index] }} />
          ))}
        </colgroup>
        <thead>
          <tr>
            {headers.map((header, index) => (
              <th
                key={`${header}-${index}`}
                className={String(header).includes('\\') ? 'diagonalTh' : ''}
              >
                {String(header).includes('\\') ? (
                  <span className="diagonalHeader">
                    <span className="diagonalTop">
                      {String(header).split('\\')[1]}
                    </span>
                    <span className="diagonalBottom">
                      {String(header).split('\\')[0]}
                    </span>
                  </span>
                ) : (
                  header
                )}

                <span
                  className="columnResizeHandle"
                  role="separator"
                  aria-orientation="vertical"
                  onMouseDown={(event) => startColumnResize(event, index)}
                  onDoubleClick={(event) => autoSizeColumn(event, index)}
                />
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {visible.map((row, rowIndex) => (
            <tr key={rowIndex} style={{ height: rowHeights[rowIndex] || undefined }}>
              {headers.map((header, columnIndex) => (
              <td key={`${header}-${columnIndex}`} className={numericColumns.includes(header) ? 'number' : ''}>
                {renderCell
                  ? renderCell({
                      row,
                      rowIndex,
                      header,
                      columnIndex,
                      value: row[header],
                    })
                  : numericColumns.includes(header)
                    ? formatNumber(row[header])
                    : String(row[header] ?? '')}

                <span
                  className="rowResizeHandle"
                  onMouseDown={(event) => startRowResize(event, rowIndex)}
                  onDoubleClick={(event) => autoSizeRow(event, rowIndex)}
                />
              </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function PickList({ title, items, selected = [], onToggle, compact = false, showChecks = true }) {
  return (
    <div className={compact ? 'pick compact' : 'pick'}>
      <div className="panelTitle">{title}</div>
      <div className="pickList">
        {items.map((item) => (
          <button type="button" key={item} className={selected.includes(item) ? 'selected' : ''} onClick={() => onToggle(item)}>
            {showChecks && selected.includes(item) ? '✓ ' : ''}{item}
          </button>
        ))}
      </div>
    </div>
  );
}

function SelectedList({ title, items, onRemove }) {
  return (
    <div className="pick compact">
      <div className="panelTitle">{title}</div>
      <div className="pickList selectedOnly">
        {items.length ? (
          items.map((item) => (
            <button type="button" key={item} className="selected" onClick={() => onRemove(item)} title="Bấm để bỏ chọn">
              ✓ {item}
            </button>
          ))
        ) : (
          <div className="emptyList">Chưa chọn cột</div>
        )}
      </div>
    </div>
  );
}

function App() {
  const dataInputRef = useRef(null);
  const folderInputRef = useRef(null);
  const jsonInputRef = useRef(null);
  const mappingInputRef = useRef(null);
  const [tab, setTab] = useState('raw');
  const [files, setFiles] = useState([]);
  const [errors, setErrors] = useState([]);
  const [isLoading, setIsLoading] = useState(false);
  const [groupColumns, setGroupColumns] = useState([]);
  const [metrics, setMetrics] = useState([]);
  const [pivotRows, setPivotRows] = useState([]);
  const [pivotColumns, setPivotColumns] = useState([]);
  const [pivotValues, setPivotValues] = useState([]);
  const [mapping, setMapping] = useState({});
  const [factors, setFactors] = useState({});
  const [outputMode, setOutputMode] = useState('flat');
  const [isExporting, setIsExporting] = useState(false);
  const [themeId, setThemeId] = useState(getInitialThemeId);
  const [isThemeOpen, setIsThemeOpen] = useState(false);
  const [sheetNameDraft, setSheetNameDraft] = useState('');

  const rawHeaders = useMemo(() => unique([FILE_NAME_COLUMN, ...files.flatMap((file) => file.headers)]), [files]);
  const rawRows = useMemo(() => files.flatMap((file) => file.rows), [files]);
  const mappedHeaders = useMemo(
    () => unique(rawHeaders.map((header) => getMappingName(mapping, header))),
    [rawHeaders, mapping],
  );
  const mappedRows = useMemo(
    () =>
      rawRows.map((row) => {
        const out = {};
        rawHeaders.forEach((header) => {
          const mappedHeader = getMappingName(mapping, header);

          if (mappedHeader.trim() === '') return;

          out[mappedHeader] = row[header] ?? '';
         });
        return out;
      }),
    [rawRows, rawHeaders, mapping],
  );
  const numericColumns = useMemo(() => getNumericColumns(mappedRows, mappedHeaders), [mappedRows, mappedHeaders]);
  const summaryGroupOptions = useMemo(
    () => mappedHeaders.filter((header) => !groupColumns.includes(header) && !numericColumns.includes(header)),
    [mappedHeaders, groupColumns, numericColumns],
  );
  const summaryRows = useMemo(() => buildSummary(mappedRows, groupColumns, metrics, factors), [mappedRows, groupColumns, metrics, factors]);
  const pivot = useMemo(() => buildPivot(mappedRows, pivotRows, pivotColumns, pivotValues), [mappedRows, pivotRows, pivotColumns, pivotValues]);
  const activeTheme = useMemo(() => themes.find((theme) => theme.id === themeId) || themes[0], [themeId]);
  const themeVars = useMemo(() => getThemeVars(activeTheme), [activeTheme]);
  const sanitizedSheetName = useMemo(() => sanitizeSheetName(sheetNameDraft), [sheetNameDraft]);
  const summarySheetName = sanitizedSheetName || DEFAULT_SUMMARY_SHEET_NAME;
  const canExportSummary = summaryRows.length > 0 && !isExporting;

  function changeTheme(nextThemeId) {
    setThemeId(nextThemeId);
    setIsThemeOpen(false);
    localStorage.setItem('txt-convert-theme', nextThemeId);
  }

  async function handleFiles(fileList) {
    const candidates = Array.from(fileList || []).filter((file) => /\.(txt|csv)$/i.test(file.name));
    setIsLoading(true);
    setErrors([]);

    try {
      const parsed = [];
      const failures = [];

      for (const file of candidates) {
        try {
          parsed.push(await parseFile(file));
        } catch (error) {
          failures.push(`${file.name}: ${error.message}`);
        }
      }

      const headers = unique([FILE_NAME_COLUMN, ...parsed.flatMap((file) => file.headers)]);
      const rows = parsed.flatMap((file) => file.rows);
      const nums = getNumericColumns(rows, headers);
      const defaultSummary = getDefaultSummary(headers, nums);

      setFiles(parsed);
      setErrors(failures);
      setGroupColumns(defaultSummary.groupColumns);
      setMetrics(defaultSummary.metrics);
      setPivotRows([]);
      setPivotColumns([]);
      setPivotValues([]);
      setFactors(Object.fromEntries(nums.map((column) => [column, 1])));
      setTab('raw');
    } finally {
      setIsLoading(false);
    }
  }
  
  function getMappingName(mapping, header) {
    return Object.prototype.hasOwnProperty.call(mapping, header)
      ? mapping[header]
      : header;
  }

  function toggle(setter, values, item) {
    setter(values.includes(item) ? values.filter((value) => value !== item) : [...values, item]);
  }

  function addGroupColumn(item) {
    if (numericColumns.includes(item)) return;
    setGroupColumns(groupColumns.includes(item) ? groupColumns : [...groupColumns, item]);
    setMetrics(metrics.filter((metric) => metric !== item));
  }

  function addMetric(item) {
    setMetrics(metrics.includes(item) ? metrics : [...metrics, item]);
    setGroupColumns(groupColumns.filter((column) => column !== item));
  }

  function reset() {
    setFiles([]);
    setErrors([]);
    setGroupColumns([]);
    setMetrics([]);
    setPivotRows([]);
    setPivotColumns([]);
    setPivotValues([]);
    setMapping({});
    setFactors({});
    setTab('raw');
    setSheetNameDraft('');
  }

  function saveConfig() {
    downloadText(
      'txt-convert-config.json',
      JSON.stringify({ groupColumns, metrics, pivotRows, pivotColumns, pivotValues, mapping, factors, outputMode }, null, 2),
    );
  }

  function openConfigPicker() {
    if (!jsonInputRef.current) return;
    jsonInputRef.current.value = '';
    jsonInputRef.current.click();
  }

  function openMappingPicker() {
    if (!mappingInputRef.current) return;
    mappingInputRef.current.value = '';
    mappingInputRef.current.click();
  }

  async function loadConfig(file, input) {
    if (!file) return;
    try {
      const data = JSON.parse(await file.text());
      const loadedMetrics = Array.isArray(data.metrics) ? data.metrics : [];
      setGroupColumns(
        Array.isArray(data.groupColumns)
          ? data.groupColumns.filter((column) => !loadedMetrics.includes(column) && !numericColumns.includes(column))
          : [],
      );
      setMetrics(loadedMetrics);
      setPivotRows(Array.isArray(data.pivotRows) ? data.pivotRows : []);
      setPivotColumns(Array.isArray(data.pivotColumns) ? data.pivotColumns : []);
      setPivotValues(Array.isArray(data.pivotValues) ? data.pivotValues : []);
      setMapping(data.mapping || {});
      setFactors(data.factors || {});
      setOutputMode(data.outputMode || 'flat');
    } finally {
      if (input) input.value = '';
    }
  }

  async function loadMapping(file, input) {
    if (!file) return;
    try {
      const data = JSON.parse(await file.text());
      setMapping(data.mapping || data || {});
    } finally {
      if (input) input.value = '';
    }
  }

  async function exportExcel(target = 'summary', summarySheetName = DEFAULT_SUMMARY_SHEET_NAME) {
    if (isExporting) return;

    setIsExporting(true);
    try {
      const XLSX = await import('xlsx-js-style');
      const workbook = XLSX.utils.book_new();

      if (target === 'raw' || target === 'all') appendSheet(XLSX, workbook, 'Du lieu goc', mappedRows, mappedHeaders);
      if (target === 'summary' || target === 'all') {
        appendSheet(XLSX, workbook, summarySheetName, summaryRows, [...groupColumns, ...metrics]);
        if (outputMode === 'byFile') {
          files.forEach((file) => {
            const names = new Set(file.rows.map((row) => row[FILE_NAME_COLUMN]));
            const rows = mappedRows.filter((row) => names.has(row[FILE_NAME_COLUMN]));
            appendSheet(XLSX, workbook, file.name.replace(/\.txt$/i, ''), buildSummary(rows, groupColumns, metrics, factors), [...groupColumns, ...metrics]);
          });
        }
      }
      if (target === 'pivot' || target === 'all') appendSheet(XLSX, workbook, 'Pivot', pivot.rows, pivot.headers);

      XLSX.writeFile(workbook, `TONG_HOP_KHOI_LUONG_${new Date().toISOString().slice(0, 10)}.xlsx`);
    } catch (error) {
      window.alert(`Không thể xuất Excel: ${error.message}`);
    } finally {
      setIsExporting(false);
    }
  }

  return (
    <main className="desktop" style={themeVars}>
      <nav className="tabs">
        <div className="brandMark" aria-label="TXT Convert">
          <img src={headerLogo} alt="" />
        </div>
        {tabs.map(([id, label]) => (
          <button type="button" key={id} className={tab === id ? 'active' : ''} onClick={() => setTab(id)}>
            {label}
          </button>
        ))}
        <div className="navTools">
          {tab === 'summary' && (
            <>
              <label style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 13, color: 'var(--muted)', whiteSpace: 'nowrap' }}>
                Xuất file:
                <select value={outputMode} onChange={(e) => setOutputMode(e.target.value)} style={{ height: 40, borderRadius: 6 }}>
                  <option value="flat">Bảng phẳng</option>
                  <option value="byFile">Tách sheet theo file</option>
                </select>
              </label>
              <button
                type="button"
                style={{ background: 'var(--accent)', color: '#fff', borderColor: 'var(--accent)' }}
                onClick={() => exportExcel('summary', summarySheetName)}
                disabled={!canExportSummary}
              >
                <Download size={15} /> {isExporting ? 'Đang xuất...' : 'Export Excel'}
              </button>
            </>
          )}
          <button type="button" className="resetButton" onClick={reset} title="Reset">
            <RotateCcw size={16} /> Reset
          </button>
          <div className="themeSetting">
            <button
              type="button"
              className="themeButton"
              onClick={() => setIsThemeOpen(!isThemeOpen)}
              title="Đổi theme màu"
              aria-expanded={isThemeOpen}
            >
              <Palette size={16} />
              <span className="themeSwatch" style={{ background: activeTheme.accent }} />
            </button>
            {isThemeOpen && (
              <div className="themePanel">
                <div className="panelTitle">Theme màu</div>
                <div className="themeChoices">
                  {themes.map((theme) => (
                    <button
                      type="button"
                      key={theme.id}
                      className={theme.id === activeTheme.id ? 'selected' : ''}
                      onClick={() => changeTheme(theme.id)}
                    >
                      <span className="themeSwatch" style={{ background: theme.accent }} />
                      {theme.name}
                    </button>
                  ))}
                </div>
              </div>
            )}
          </div>
        </div>
      </nav>

      <section className="workspace">
        {tab === 'raw' && (
          <>
            <div className="fileBar">
              <button type="button" onClick={() => dataInputRef.current?.click()}>
                <FolderOpen size={16} /> Chọn files Excel/Txt/CSV
              </button>
              <button type="button" onClick={() => folderInputRef.current?.click()}>
                <FolderOpen size={16} /> Chọn folder TXT
              </button>
              <button type="button" onClick={() => exportExcel('raw')} disabled={!mappedRows.length || isExporting}>
                <Download size={16} /> Export dữ liệu gốc
              </button>
              <input ref={dataInputRef} type="file" accept=".txt,.csv" multiple onChange={(e) => handleFiles(e.target.files)} />
              <input ref={folderInputRef} type="file" accept=".txt,.csv" multiple webkitdirectory="" onChange={(e) => handleFiles(e.target.files)} />
            </div>
            <div className="hint">
              {isLoading ? 'Đang đọc dữ liệu...' : `Đã chọn file: ${files.map((file) => file.name).join(', ') || 'chưa có'}`}
            </div>
            {!!errors.length && <div className="error">{errors.join(' | ')}</div>}
            <Table headers={mappedHeaders} rows={mappedRows} numericColumns={numericColumns} />
          </>
        )}

        {tab === 'summary' && (
          <>
            <div className="configGrid">
              <PickList
                title="Các cột có sẵn:"
                items={summaryGroupOptions}
                selected={[]}
                showChecks={false}
                onToggle={addGroupColumn}
              />
              <div className="rightConfig">
                <SelectedList title="Group theo:" items={groupColumns} onRemove={(item) => toggle(setGroupColumns, groupColumns, item)} />
                <div className="buttonRow">
                  <button type="button" onClick={saveConfig}><FileJson size={15} /> Lưu</button>
                  <button type="button" onClick={openConfigPicker}><Upload size={15} /> Load</button>
                  <input ref={jsonInputRef} type="file" accept=".json" onChange={(e) => loadConfig(e.target.files?.[0], e.target)} />
                  <label className="sheetNameInline">
                    <input
                      className="tableInput"
                      value={sheetNameDraft}
                      maxLength={31}
                      placeholder="Tên sheet đầu tiên"
                      onChange={(event) => setSheetNameDraft(event.target.value)}
                    />
                  </label>
                </div>
              </div>
              <SelectedList title="Chỉ tiêu tổng hợp:" items={metrics} onRemove={(item) => toggle(setMetrics, metrics, item)} />
              <div className="exportPanel">
                <PickList
                  title="Thêm chỉ tiêu:"
                  items={numericColumns.filter((column) => !metrics.includes(column))}
                  selected={[]}
                  showChecks={false}
                  onToggle={addMetric}
                  compact
                />
              </div>
            </div>
            <div className="previewTitle">📊 Preview Table:</div>
            <Table headers={[...groupColumns, ...metrics]} rows={summaryRows} numericColumns={metrics} />
          </>
        )}

        {tab === 'pivot' && (
          <div className="pivotWorkspace">
            <div className="pivotGrid">
              <PickList title="Các cột có sẵn:" items={mappedHeaders} selected={[...pivotRows, ...pivotColumns, ...pivotValues]} onToggle={(item) => toggle(setPivotRows, pivotRows, item)} />
              <PickList title="Rows:" items={mappedHeaders} selected={pivotRows} onToggle={(item) => toggle(setPivotRows, pivotRows, item)} compact />
              <PickList title="Columns:" items={mappedHeaders} selected={pivotColumns} onToggle={(item) => toggle(setPivotColumns, pivotColumns, item)} compact />
              <PickList title="Values:" items={numericColumns} selected={pivotValues} onToggle={(item) => setPivotValues([item])} compact />
            </div>
            <div className="buttonRow bottom">
              <button type="button">Preview Pivot</button>
              <button type="button" onClick={() => exportExcel('pivot')} disabled={!pivot.rows.length || isExporting}>
                <Download size={15} /> Export Pivot to Excel
              </button>
            </div>
            <div className="pivotPreview">
              <Table headers={pivot.headers} rows={pivot.rows} numericColumns={pivot.headers.slice(pivotRows.length)} />
            </div>
          </div>
        )}

        {tab === 'factor' && (
          <>
            <div className="factorGrid">
              {numericColumns.map((column) => (
                <label key={column}>
                  {column}
                  <input type="number" step="0.01" value={factors[column] ?? 1} onChange={(e) => setFactors({ ...factors, [column]: e.target.value })} />
                </label>
              ))}
            </div>
            <div className="hint">Hệ số nhân được áp dụng khi tổng hợp và xuất Excel.</div>
          </>
        )}

        {tab === 'mapping' && (
          <>
            <Table
              headers={['Tên cột gốc', 'Mapping thành']}
              rows={rawHeaders
                .filter((header) => header !== FILE_NAME_COLUMN)
                .map((header) => ({
                  'Tên cột gốc': header,
                  'Mapping thành': getMappingName(mapping, header),
                }))}
              renderCell={({ row, header }) => {
                const originalHeader = row['Tên cột gốc'];

                if (header === 'Mapping thành') {
                  return (
                    <input
                      className="tableInput"
                      value={getMappingName(mapping, originalHeader)}
                      onChange={(e) =>
                        setMapping((current) => ({
                          ...current,
                          [originalHeader]: e.target.value,
                        }))
                      }
                      onClick={(e) => e.stopPropagation()}
                    />
                  );
                }

                return String(row[header] ?? '');
              }}
            />

            <div className="buttonRow bottom">
              <button type="button" onClick={() => setMapping({})}>
                Áp dụng Mapping mặc định
              </button>

              <button
                type="button"
                onClick={() => downloadText('txt-convert-mapping.json', JSON.stringify({ mapping }, null, 2))}
              >
                <FileJson size={15} /> Lưu Mapping ra file JSON
              </button>

              <button type="button" onClick={openMappingPicker}>
                <Upload size={15} /> Load Mapping từ file JSON
              </button>

              <input
                ref={mappingInputRef}
                type="file"
                accept=".json"
                onChange={(e) => loadMapping(e.target.files?.[0], e.target)}
              />
            </div>
          </>
        )}
      </section>

      <footer>
        Sử dụng: {formatNumber(mappedRows.length)} dòng | File: {files.length} | Group: {groupColumns.join(', ') || '-'} | Chỉ tiêu:{' '}
        {metrics.join(', ') || '-'}
      </footer>

    </main>
  );
}

function LoginScreen({ onSuccess }) {
  const [email, setEmail] = useState('');
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);

  const submit = async () => {
    if (loading) return;
    setError('');
    setLoading(true);
    try {
      const result = await window.electronBridge?.checkAuth?.(email);
      if (result?.ok) {
        onSuccess(result);
      } else {
        setError(result?.error || 'Không thể đăng nhập.');
      }
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="loginScreen">
      <div className="loginCard">
        <img src={headerLogo} alt="BIMLab" className="loginLogo" />
        <h2>Đăng nhập</h2>
        <p>Nhập Gmail đã được cấp quyền sử dụng công cụ.</p>
        <input
          type="email"
          className="loginInput"
          placeholder="ten@gmail.com"
          value={email}
          onChange={(e) => setEmail(e.target.value)}
          onKeyDown={(e) => e.key === 'Enter' && submit()}
          disabled={loading}
          autoFocus
        />
        {error && <div className="loginError">{error}</div>}
        <button type="button" className="loginButton" onClick={submit} disabled={loading}>
          {loading ? 'Đang kiểm tra...' : 'Đăng nhập'}
        </button>
      </div>
    </div>
  );
}

function Root() {
  const [session, setSession] = useState(null);

  useEffect(() => {
    // Báo cho main process là đã có nội dung để hiện cửa sổ - phải gọi ngay
    // cả khi đang ở màn hình đăng nhập, vì cửa sổ đang ẩn (show:false) chờ
    // tín hiệu này.
    window.electronBridge?.appReady(true);
  }, []);

  if (!session) {
    return <LoginScreen onSuccess={setSession} />;
  }

  return <App />;
}

createRoot(document.getElementById('root')).render(<Root />);
