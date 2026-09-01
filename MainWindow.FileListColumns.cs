using System.Windows;
using System.Windows.Controls;
using ParallelScope.Utilities;

namespace ParallelScope;

/// <summary>ファイル一覧の表示列（見出し・表示/非表示・並び順・列幅）に関する処理。</summary>
public partial class MainWindow
{
    // XAML定義の既定の列幅。設定画面の「Reset column widths」で戻すため、保存済み幅を反映する前に控えておく
    private readonly Dictionary<string, DataGridLength> _defaultFileListColumnWidths;

    // ファイル一覧の列見出しを現在の言語で設定する。
    // DataGridColumn は表示ツリーの外にあるため、XAMLのバインディングではなくコードから直接入れる
    private void ApplyFileListColumnHeaders()
    {
        foreach (var (key, column) in GetFileListColumnsByKey())
        {
            column.Header = UiText.Get($"Column.{key}");
        }
    }

    // 実際にファイル一覧へ表示しているオプション列を画面上の列順で返す（購読状態による絞り込みも含む）
    private IReadOnlyList<string> GetEffectiveVisibleColumns()
    {
        return FileListColumns.GetEffectiveVisibleColumns(
            _viewModel.GetVisibleColumns(),
            _storeLicenseService.IsPlusActive);
    }

    // 設定された表示列に合わせて、ファイル一覧のオプション列の表示/非表示を切り替える（Name列は常時表示）
    private void ApplyFileListColumnVisibility()
    {
        var visibleColumns = GetEffectiveVisibleColumns().ToHashSet(StringComparer.OrdinalIgnoreCase);

        SetColumnVisibility(LocationColumn, visibleColumns.Contains(FileListColumns.Location));
        SetColumnVisibility(TypeColumn, visibleColumns.Contains(FileListColumns.Type));
        SetColumnVisibility(SizeColumn, visibleColumns.Contains(FileListColumns.Size));
        SetColumnVisibility(ModifiedColumn, visibleColumns.Contains(FileListColumns.Modified));
        SetColumnVisibility(CreatedColumn, visibleColumns.Contains(FileListColumns.Created));
        SetColumnVisibility(AttributesColumn, visibleColumns.Contains(FileListColumns.Attributes));
    }

    private static void SetColumnVisibility(DataGridColumn column, bool isVisible)
    {
        column.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    // 列キーとファイル一覧の列を対応付ける（並び順・列幅の保存/復元で使う）
    private Dictionary<string, DataGridColumn> GetFileListColumnsByKey()
    {
        return new Dictionary<string, DataGridColumn>(StringComparer.OrdinalIgnoreCase)
        {
            [FileListColumns.Name] = NameColumn,
            [FileListColumns.Location] = LocationColumn,
            [FileListColumns.Type] = TypeColumn,
            [FileListColumns.Size] = SizeColumn,
            [FileListColumns.Modified] = ModifiedColumn,
            [FileListColumns.Created] = CreatedColumn,
            [FileListColumns.Attributes] = AttributesColumn
        };
    }

    // 保存済みの列の並び順・列幅を反映する。どちらもPlus機能のため、
    // 未購読（購読期限切れ含む）の間は保存済み設定を無視してXAML定義のままにする
    private void ApplyFileListColumnLayout()
    {
        if (!_storeLicenseService.IsPlusActive)
        {
            return;
        }

        var columnsByKey = GetFileListColumnsByKey();

        // DisplayIndexは代入のたびに他の列がずれるため、目的の並び順で先頭から詰め直す
        var displayIndex = 0;
        foreach (var columnKey in _viewModel.GetColumnOrder())
        {
            if (columnsByKey.TryGetValue(columnKey, out var column))
            {
                column.DisplayIndex = displayIndex++;
            }
        }

        foreach (var (columnKey, width) in _viewModel.GetColumnWidths())
        {
            if (columnsByKey.TryGetValue(columnKey, out var column))
            {
                column.Width = new DataGridLength(width);
            }
        }
    }

    // 保存済みの列幅を破棄し、ファイル一覧の列幅をXAML定義の既定値へ戻す
    private void ResetFileListColumnWidths()
    {
        _viewModel.ResetColumnWidths();

        foreach (var (columnKey, column) in GetFileListColumnsByKey())
        {
            if (_defaultFileListColumnWidths.TryGetValue(columnKey, out var defaultWidth))
            {
                column.Width = defaultWidth;
            }
        }
    }

    // 現在の列の並び順・列幅を保存する。ヘッダーのドラッグ操作は個別に拾わず、
    // ウィンドウを閉じる時に最終状態をまとめて保存する
    private void SaveFileListColumnLayout()
    {
        if (!_storeLicenseService.IsPlusActive)
        {
            return;
        }

        var columnsByKey = GetFileListColumnsByKey();
        var columnOrder = columnsByKey
            .OrderBy(pair => pair.Value.DisplayIndex)
            .Select(pair => pair.Key)
            .ToList();

        var columnWidths = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var (columnKey, column) in columnsByKey)
        {
            // 残り幅いっぱい（*）のままの列は、幅が固定されると画面幅に追従しなくなるため保存しない
            if (column.Width.UnitType != DataGridLengthUnitType.Pixel)
            {
                continue;
            }

            // ヘッダーのドラッグでリサイズしてもDataGridLength.Valueは変わらず表示幅だけが更新されるため、
            // 実際の幅はActualWidthから読む
            var actualWidth = column.ActualWidth;
            if (double.IsFinite(actualWidth) && actualWidth > 0)
            {
                columnWidths[columnKey] = actualWidth;
            }
        }

        _viewModel.SaveColumnLayout(columnOrder, columnWidths);
    }
}
