using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using ParallelScope.Utilities;
using ParallelScope.ViewModels;

namespace ParallelScope.Views;

/// <summary>ファイル一覧（ダブルクリック・ソート・コンテキストメニュー・CSV出力）に関する処理。</summary>
public partial class BrowserPaneView
{
    // Shift+ホイールで横スクロールする（WPFのScrollViewerは既定では縦にしか反応しない）
    private void FileListDataGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Shift)
        {
            return;
        }

        if (FindScrollViewer(FileListDataGrid) is not { } scrollViewer)
        {
            return;
        }

        scrollViewer.ScrollToHorizontalOffset(scrollViewer.HorizontalOffset - e.Delta);
        e.Handled = true;
    }

    // DataGridのテンプレート内にあるScrollViewerを探す（横スクロールの操作対象）
    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer found)
        {
            return found;
        }

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            if (FindScrollViewer(VisualTreeHelper.GetChild(root, index)) is { } scrollViewer)
            {
                return scrollViewer;
            }
        }

        return null;
    }

    // 表示中のファイル一覧（検索結果・All Files表示・通常一覧のいずれも、ソート順と表示列のまま）をCSVへ書き出す。
    // Plus機能のため、未購読の間はメニュー項目自体を無効にしている（ここは念のための安全弁）
    internal async Task ExportCsvAsync()
    {
        if (!_storeLicenseService.IsPlusActive)
        {
            return;
        }

        // ヘッダークリックのソート結果を反映するため、バインド元のコレクションではなくDataGridの表示順で取り出す
        var items = FileListDataGrid.Items.OfType<FileItemViewModel>().ToList();
        if (items.Count == 0)
        {
            MessageBox.Show(UiText.Get("Csv.NoItems"), UiText.Get("Csv.Caption"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Size列の書式は保存ダイアログの「ファイルの種類」で選ばせる（選んだ書式は次回の既定になる）
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = UiText.Get("Csv.Caption"),
            Filter = UiText.Get("Csv.Filter"),
            FilterIndex = _viewModel.GetCsvExportSizeInBytes() ? 2 : 1,
            DefaultExt = ".csv",
            AddExtension = true,
            FileName = BuildCsvFileName()
        };

        var window = Window.GetWindow(this);
        if (dialog.ShowDialog(window) != true)
        {
            return;
        }

        var filePath = dialog.FileName;
        var columns = GetEffectiveVisibleColumns();
        var sizeInBytes = dialog.FilterIndex == 2;
        _viewModel.SetCsvExportSizeInBytes(sizeInBytes);

        // 待機カーソルはウィンドウ全体に出す（書き出し中はメニュー操作も待たせたい）
        if (window is not null)
        {
            window.Cursor = Cursors.Wait;
        }

        try
        {
            // 数十万行になり得るため、書き出しはバックグラウンドで行う
            // （FileItemViewModelは取得済みのスナップショットを読むだけなのでUIスレッド外から触って問題ない）
            await Task.Run(() => FileListCsvExporter.Export(filePath, items, columns, sizeInBytes));

            MessageBox.Show(
                UiText.Format("Csv.Exported", items.Count, filePath),
                UiText.Get("Csv.Caption"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(UiText.Format("Csv.ExportFailed", ex.Message), UiText.Get("Csv.Caption"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            if (window is not null)
            {
                window.Cursor = null;
            }
        }
    }

    // 既定のファイル名を "ParallelScope_<フォルダ名>_yyyyMMdd_HHmmss.csv" で組み立てる
    private string BuildCsvFileName()
    {
        var folderName = string.IsNullOrWhiteSpace(ActiveTab.CurrentPath)
            ? string.Empty
            : Path.GetFileName(ActiveTab.CurrentPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        // ドライブ直下（"D:\"）などフォルダ名が取れない場合と、パスに使えない文字を除去した結果空になる場合がある
        var sanitized = new string(folderName.Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray());
        var prefix = string.IsNullOrWhiteSpace(sanitized) ? "ParallelScope" : $"ParallelScope_{sanitized}";

        return $"{prefix}_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
    }

    // ダブルクリック時、フォルダなら中へ移動、ファイルなら関連付けアプリで開く
    private void FileListDataGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not DataGrid dataGrid)
        {
            return;
        }

        if (dataGrid.SelectedItem is not FileItemViewModel item)
        {
            return;
        }

        if (item.IsFolder)
        {
            if (ActiveTab.LoadFiles(item.FullPath))
            {
                SyncTreeSelectionToCurrentPath();
            }
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = item.FullPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(UiText.Format("File.OpenFailed", ex.Message), UiText.Get("Dialog.Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // 中クリックされたフォルダ行を新しいタブで開く
    private void FileListDataGrid_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle)
        {
            return;
        }

        var row = GetAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row?.Item is FileItemViewModel { IsFolder: true } item)
        {
            OpenPathInNewTab(item.FullPath);
            e.Handled = true;
        }
    }

    // 右クリックされた行を選択状態にしてからコンテキストメニューを表示する
    private void FileListDataGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var row = GetAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row is null || row.IsSelected)
        {
            // 既に選ばれている行の右クリックでは、選択（複数選択も含む）をそのまま保つ
            return;
        }

        // DataGridの既定は複数選択（Extended）のため、IsSelectedを立てるだけでは
        // 左クリックで選んでいた行が選択されたまま残り、SelectedItem もそちらを指し続ける
        FileListDataGrid.UnselectAll();
        row.IsSelected = true;
        row.Focus();
    }

    // Size/Modified/Created 列のヘッダークリック時のソートを、生値（バイト数・日時）比較の
    // カスタムソートに差し替える。これらの列の表示文字列は保持されず表示時に生成されるため、
    // 既定の（表示プロパティ経由の）ソートだと比較のたびに全行分の文字列生成が走ってしまう。
    // 生値比較は文字列比較より速く、Size列は数値順（既定の文字列順では "9 KB" > "12 MB" となる）で並ぶ
    private void FileListDataGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        var columnKey = GetColumnKey(e.Column);
        if (columnKey is null)
        {
            return;
        }

        var isAscending = e.Column.SortDirection != ListSortDirection.Ascending;

        // 並びはタブごとの状態。DataGrid側はタブを切り替えると失われるため、タブに控えて復元できるようにする
        ActiveTab.SortColumnKey = columnKey;
        ActiveTab.IsSortAscending = isAscending;

        if (GetSortComparison(columnKey) is null)
        {
            // その他の列は既定のソートに任せる。カスタムソートが残っていると
            // SortDescriptions より優先されてしまうため解除しておく
            if (CollectionViewSource.GetDefaultView(FileListDataGrid.ItemsSource) is ListCollectionView defaultView)
            {
                defaultView.CustomSort = null;
            }

            return;
        }

        e.Handled = true;
        ApplySort(columnKey, isAscending);
    }

    /// <summary>表示中のタブに控えてあるソート順を、一覧へ反映し直す（タブ切り替え時）。</summary>
    private void ApplyActiveTabSort()
    {
        var tab = ActiveTab;
        if (tab.SortColumnKey is not { } columnKey)
        {
            // ソート指定の無いタブは既定の並び（取得順）に戻す
            if (CollectionViewSource.GetDefaultView(FileListDataGrid.ItemsSource) is ListCollectionView view)
            {
                view.CustomSort = null;
                view.SortDescriptions.Clear();
            }

            foreach (var column in FileListDataGrid.Columns)
            {
                column.SortDirection = null;
            }

            return;
        }

        ApplySort(columnKey, tab.IsSortAscending);
    }

    /// <summary>指定列でファイル一覧を並べ替え、ヘッダーの矢印表示も合わせる。</summary>
    private void ApplySort(string columnKey, bool isAscending)
    {
        if (CollectionViewSource.GetDefaultView(FileListDataGrid.ItemsSource) is not ListCollectionView view)
        {
            return;
        }

        var sortedColumn = GetFileListColumnsByKey().GetValueOrDefault(columnKey);
        var direction = isAscending ? ListSortDirection.Ascending : ListSortDirection.Descending;

        // e.Handled = true にすると既定処理によるヘッダーの矢印表示の更新も行われないため、自前で反映する
        foreach (var column in FileListDataGrid.Columns)
        {
            column.SortDirection = ReferenceEquals(column, sortedColumn) ? direction : null;
        }

        var compareAscending = GetSortComparison(columnKey);
        if (compareAscending is null)
        {
            // 生値比較を持たない列は、表示プロパティでの既定のソートに任せる
            view.CustomSort = null;
            view.SortDescriptions.Clear();
            if (sortedColumn?.SortMemberPath is { Length: > 0 } sortMemberPath)
            {
                view.SortDescriptions.Add(new SortDescription(sortMemberPath, direction));
            }

            return;
        }

        // 既定ソートで積まれた SortDescriptions が残っていると意図しない並びになるため消しておく
        view.SortDescriptions.Clear();
        view.CustomSort = isAscending
            ? Comparer<FileItemViewModel>.Create(compareAscending)
            : Comparer<FileItemViewModel>.Create((a, b) => compareAscending(b, a));
    }

    /// <summary>
    /// Name/Size/Modified/Created 列の比較関数（生値での比較）。表示文字列は保持されず表示時に生成されるため、
    /// 既定の（表示プロパティ経由の）ソートだと比較のたびに全行分の文字列生成が走ってしまう。
    /// 生値比較は文字列比較より速く、Size列は数値順（既定の文字列順では "9 KB" > "12 MB" となる）で並ぶ。
    /// 対象外の列は null を返し、既定のソートに任せる。
    /// </summary>
    private static Comparison<FileItemViewModel>? GetSortComparison(string columnKey)
    {
        if (string.Equals(columnKey, FileListColumns.Name, StringComparison.OrdinalIgnoreCase))
        {
            // 初期表示と同じ「フォルダを先に、次に名前順」で並べる（降順では全体が反転してフォルダが末尾側になる）
            return (a, b) =>
            {
                var folderCompare = b.IsFolder.CompareTo(a.IsFolder);
                return folderCompare != 0
                    ? folderCompare
                    : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            };
        }

        if (string.Equals(columnKey, FileListColumns.Size, StringComparison.OrdinalIgnoreCase))
        {
            // サイズ未取得（null）は最小として先頭に寄せる
            return (a, b) => (a.SizeBytes ?? -1L).CompareTo(b.SizeBytes ?? -1L);
        }

        if (string.Equals(columnKey, FileListColumns.Modified, StringComparison.OrdinalIgnoreCase))
        {
            return (a, b) => a.ModifiedAt.CompareTo(b.ModifiedAt);
        }

        if (string.Equals(columnKey, FileListColumns.Created, StringComparison.OrdinalIgnoreCase))
        {
            return (a, b) => (a.CreatedAt ?? DateTime.MinValue).CompareTo(b.CreatedAt ?? DateTime.MinValue);
        }

        return null;
    }

    /// <summary>ファイル一覧の列に対応する列キーを返す（未知の列は null）。</summary>
    private string? GetColumnKey(DataGridColumn column)
    {
        foreach (var (key, candidate) in GetFileListColumnsByKey())
        {
            if (ReferenceEquals(candidate, column))
            {
                return key;
            }
        }

        return null;
    }

    // 行以外（空白部分・列ヘッダー）ではコンテキストメニューを表示しない
    private void FileListDataGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var row = GetAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row is null || row.Item is not FileItemViewModel item)
        {
            e.Handled = true;
            return;
        }

        // 「新しいタブで開く」「反対側のペインで開く」はフォルダ行のときだけ（Plus未購読の間は常に無効）
        OpenInNewTabMenuItem.IsEnabled = _areTabsEnabled && item.IsFolder && _paneViewModel.CanAddTab;
        // 1画面のときは反対側のペインが無いが、その場合はクリック時に分割して開くので選べる状態にする
        var otherPane = _viewModel.GetOtherPane(_paneViewModel);
        OpenInOtherPaneMenuItem.IsEnabled = _areTabsEnabled && item.IsFolder && (otherPane?.CanAddTab ?? true);

        // 「このペインを閉じる」は2画面のときしか意味が無いので、1画面では区切り線ごと隠す
        var closePaneVisibility = otherPane is null ? Visibility.Collapsed : Visibility.Visible;
        ClosePaneSeparator.Visibility = closePaneVisibility;
        ClosePaneMenuItem.Visibility = closePaneVisibility;
    }

    // 右クリックメニューから、このペインを閉じて1画面に戻す
    private void ClosePaneMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _host.ClosePane(_paneViewModel, moveTabs: true);
    }

    // 右クリックメニューから、選択中のフォルダを反対側のペインで開く
    private void OpenInOtherPaneMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (GetSelectedFileItem() is { IsFolder: true } item)
        {
            OpenPathInOtherPane(item.FullPath);
        }
    }

    // 右クリックメニューから、選択中のフォルダを新しいタブで開く
    private void OpenInNewTabMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (GetSelectedFileItem() is { IsFolder: true } item)
        {
            OpenPathInNewTab(item.FullPath);
        }
    }

    private FileItemViewModel? GetSelectedFileItem()
    {
        return FileListDataGrid.SelectedItem as FileItemViewModel;
    }

    // 選択中のファイル/フォルダを、エクスプローラーに貼り付け可能な形式でクリップボードへコピーする
    private void CopyFileMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (GetSelectedFileItem() is not { } item)
        {
            return;
        }

        TrySetClipboard(() =>
        {
            var files = new System.Collections.Specialized.StringCollection { item.FullPath };
            Clipboard.SetFileDropList(files);
        });
    }

    private void CopyFileNameMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (GetSelectedFileItem() is not { } item)
        {
            return;
        }

        TrySetClipboard(() => Clipboard.SetText(item.Name));
    }

    private void CopyFullPathMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (GetSelectedFileItem() is not { } item)
        {
            return;
        }

        TrySetClipboard(() => Clipboard.SetText(item.FullPath));
    }

    // 選択中のアイテムの親フォルダをWindowsのエクスプローラーで開き、アイテムを選択状態にする
    private void OpenParentFolderMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (GetSelectedFileItem() is not { } item)
        {
            return;
        }

        try
        {
            // /select は対象の親フォルダを開いて対象を選択する。パスが存在しない場合でも
            // explorer.exe は既定フォルダを開くだけでエラーにならないため、事前に存在確認する
            if (!File.Exists(item.FullPath) && !Directory.Exists(item.FullPath))
            {
                MessageBox.Show(UiText.Get("ParentFolder.Missing"), UiText.Get("ParentFolder.Caption"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{item.FullPath}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(UiText.Format("ParentFolder.OpenFailed", ex.Message), UiText.Get("ParentFolder.Caption"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // クリップボードは他プロセスがロックしていると失敗（COMException）することがあるため、エラー表示に集約する
    private void TrySetClipboard(Action setAction)
    {
        try
        {
            setAction();
        }
        catch (Exception ex)
        {
            MessageBox.Show(UiText.Format("Clipboard.Failed", ex.Message), UiText.Get("Clipboard.Caption"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
