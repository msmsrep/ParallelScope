using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using ParallelScope.Utilities;
using ParallelScope.ViewModels;

namespace ParallelScope;

/// <summary>ファイル一覧（ダブルクリック・ソート・コンテキストメニュー・CSV出力）に関する処理。</summary>
public partial class MainWindow
{
    // 表示中のファイル一覧（検索結果・All Files表示・通常一覧のいずれも、ソート順と表示列のまま）をCSVへ書き出す。
    // Plus機能のため、未購読の間はメニュー項目自体を無効にしている（ここは念のための安全弁）
    private async void ExportCsvMenuItem_Click(object sender, RoutedEventArgs e)
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

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var filePath = dialog.FileName;
        var columns = GetEffectiveVisibleColumns();
        var sizeInBytes = dialog.FilterIndex == 2;
        _viewModel.SetCsvExportSizeInBytes(sizeInBytes);

        Cursor = Cursors.Wait;
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
            Cursor = null;
        }
    }

    // 既定のファイル名を "ParallelScope_<フォルダ名>_yyyyMMdd_HHmmss.csv" で組み立てる
    private string BuildCsvFileName()
    {
        var folderName = string.IsNullOrWhiteSpace(_viewModel.CurrentPath)
            ? string.Empty
            : Path.GetFileName(_viewModel.CurrentPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

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
            if (_viewModel.LoadFiles(item.FullPath))
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

    // 右クリックされた行を選択状態にしてからコンテキストメニューを表示する
    private void FileListDataGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var row = GetAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row is null)
        {
            return;
        }

        row.IsSelected = true;
        row.Focus();
    }

    // Size/Modified/Created 列のヘッダークリック時のソートを、生値（バイト数・日時）比較の
    // カスタムソートに差し替える。これらの列の表示文字列は保持されず表示時に生成されるため、
    // 既定の（表示プロパティ経由の）ソートだと比較のたびに全行分の文字列生成が走ってしまう。
    // 生値比較は文字列比較より速く、Size列は数値順（既定の文字列順では "9 KB" > "12 MB" となる）で並ぶ
    private void FileListDataGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        if (CollectionViewSource.GetDefaultView(FileListDataGrid.ItemsSource) is not ListCollectionView view)
        {
            return;
        }

        Comparison<FileItemViewModel>? compareAscending = null;
        if (ReferenceEquals(e.Column, NameColumn))
        {
            // 初期表示と同じ「フォルダを先に、次に名前順」で並べる（降順では全体が反転してフォルダが末尾側になる）
            compareAscending = (a, b) =>
            {
                var folderCompare = b.IsFolder.CompareTo(a.IsFolder);
                return folderCompare != 0
                    ? folderCompare
                    : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            };
        }
        else if (ReferenceEquals(e.Column, SizeColumn))
        {
            // サイズ未取得（null）は最小として先頭に寄せる
            compareAscending = (a, b) => (a.SizeBytes ?? -1L).CompareTo(b.SizeBytes ?? -1L);
        }
        else if (ReferenceEquals(e.Column, ModifiedColumn))
        {
            compareAscending = (a, b) => a.ModifiedAt.CompareTo(b.ModifiedAt);
        }
        else if (ReferenceEquals(e.Column, CreatedColumn))
        {
            compareAscending = (a, b) => (a.CreatedAt ?? DateTime.MinValue).CompareTo(b.CreatedAt ?? DateTime.MinValue);
        }

        if (compareAscending is null)
        {
            // その他の列は既定のソートに任せる。カスタムソートが残っていると
            // SortDescriptions より優先されてしまうため解除しておく
            view.CustomSort = null;
            return;
        }

        e.Handled = true;

        var direction = e.Column.SortDirection != ListSortDirection.Ascending
            ? ListSortDirection.Ascending
            : ListSortDirection.Descending;

        // e.Handled = true にすると既定処理によるヘッダーの矢印表示の更新も行われないため、自前で反映する
        foreach (var column in FileListDataGrid.Columns)
        {
            column.SortDirection = ReferenceEquals(column, e.Column) ? direction : null;
        }

        // 既定ソートで積まれた SortDescriptions が残っていると意図しない並びになるため消しておく
        view.SortDescriptions.Clear();
        view.CustomSort = direction == ListSortDirection.Ascending
            ? Comparer<FileItemViewModel>.Create(compareAscending)
            : Comparer<FileItemViewModel>.Create((a, b) => compareAscending(b, a));
    }

    // 行以外（空白部分・列ヘッダー）ではコンテキストメニューを表示しない
    private void FileListDataGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var row = GetAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row is null || row.Item is not FileItemViewModel)
        {
            e.Handled = true;
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
