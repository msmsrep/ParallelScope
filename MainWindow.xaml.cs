using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ParallelScope.Services;
using ParallelScope.Utilities;
using ParallelScope.ViewModels;

namespace ParallelScope;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly StoreLicenseService _storeLicenseService = new();
    private readonly DispatcherTimer _scheduledFullScanTimer;
    private bool _hasStartedAutomaticFullScan;
    private bool _isFullScanRunning;
    // 右クリックで押されたツリーノード（マウスを離す時点でカーソル直下が変わっても対象を保つため）
    private TreeViewItem? _rightClickedTreeViewItem;
    // XAML定義の既定の列幅。設定画面の「Reset column widths」で戻すため、保存済み幅を反映する前に控えておく
    private readonly Dictionary<string, DataGridLength> _defaultFileListColumnWidths;
    public MainWindow()
    {
        InitializeComponent();

        _defaultFileListColumnWidths = GetFileListColumnsByKey()
            .ToDictionary(pair => pair.Key, pair => pair.Value.Width, StringComparer.OrdinalIgnoreCase);

        // AppxManifest.xmlのバージョンをタイトルに付与する（取得できない場合は元のタイトルのまま）
        Title = BuildWindowTitleWithVersion(Title);

        _viewModel = new MainWindowViewModel();
        // 保存済みの言語・テーマは最初の描画前に適用する
        // （App.xamlのThemeMode="System"のままだと一瞬OSの配色で表示されてしまう）
        _viewModel.ApplySavedLanguage();
        AppTheme.Apply(_viewModel.GetTheme());
        _scheduledFullScanTimer = new DispatcherTimer();
        _scheduledFullScanTimer.Tick += ScheduledFullScanTimer_Tick;
        DataContext = _viewModel;
        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
        AppLanguage.Changed += AppLanguage_Changed;

        ApplyFileListColumnHeaders();
        ApplyFileListColumnVisibility();
        SyncTreeSelectionToCurrentPath();
    }

    // 言語切り替え時、バインディングでは追従しない箇所（ファイル一覧の列見出し）を貼り替える
    private void AppLanguage_Changed(object? sender, EventArgs e)
    {
        ApplyFileListColumnHeaders();
    }

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

    // お気に入り・最近・よく使うノードはPlus機能のため、購読状態に合わせてツリーへの表示を切り替える
    private void ApplyPlusTreeNodes()
    {
        _viewModel.SetPlusFeaturesEnabled(_storeLicenseService.IsPlusActive);
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

    // ウィンドウ表示後に自動フルスキャンを1回だけ実行し、以降は定期スキャンタイマーに切り替える
    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_hasStartedAutomaticFullScan)
        {
            return;
        }

        _hasStartedAutomaticFullScan = true;

        // Plusの購読状態を確認し、購読済みならユーザー設定の表示列を反映し直す
        // （コンストラクタ時点ではライセンス未取得のためデフォルト列で表示されている）。
        // settings.jsonに開発者キーが設定されていればStoreの購読状態に関わらずPlusを有効化する
        _storeLicenseService.ApplyDeveloperUnlockKey(_viewModel.GetDeveloperUnlockKey());
        await _storeLicenseService.RefreshLicenseAsync();
        ApplyFileListColumnVisibility();
        ApplyFileListColumnLayout();
        ApplyPlusTreeNodes();

        await RunAutomaticFullScanAsync();
        ConfigureScheduledFullScanTimer();
    }

    // ウィンドウクローズ時に定期スキャンタイマーを停止する
    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        try
        {
            SaveFileListColumnLayout();
        }
        catch
        {
            // 設定ファイルが書けない状況でも終了処理は続行する
        }

        _scheduledFullScanTimer.Stop();
        _scheduledFullScanTimer.Tick -= ScheduledFullScanTimer_Tick;
        AppLanguage.Changed -= AppLanguage_Changed;
    }

    // "アプリ名" を "アプリ名 vX.Y.Z.W" に組み立てる。バージョンが取得できない場合は元のタイトルのまま返す
    private static string BuildWindowTitleWithVersion(string baseTitle)
    {
        var version = AppVersionProvider.GetVersion();
        return string.IsNullOrWhiteSpace(version) ? baseTitle : $"{baseTitle}  ver{version}";
    }
    private readonly Dictionary<string, TreeViewItem> _treeItemMap = new(StringComparer.OrdinalIgnoreCase);
    // 生成されたTreeViewItemをパスで引けるように記録する（ツリー選択の同期に使用）
    private void FolderTreeViewItem_Loaded(object sender, RoutedEventArgs e)
    {
        // お気に入り／よく使い配下は実体ツリーの複製なので登録しない
        // （同じパスで上書きされると、実体ツリーで選択したつもりが複製側へ飛んでしまう）
        if (sender is TreeViewItem tvi && tvi.DataContext is FolderItemViewModel { IsShortcut: false } vm)
        {
            _treeItemMap[vm.Path] = tvi;
        }
    }

    // ツリーから外れたTreeViewItem（設定変更でのルート再構築・子の再読み込みで破棄されたもの）への
    // 参照をマップに残さない（残すと配下のビジュアルツリーごと解放されず、メモリが増え続ける）。
    // 同一パスに新しいインスタンスが登録済みの場合は消さない（Loaded→古い方のUnloadedの順で届くことがあるため）
    private void FolderTreeViewItem_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TreeViewItem tvi)
        {
            return;
        }

        if (tvi.DataContext is FolderItemViewModel vm)
        {
            if (_treeItemMap.TryGetValue(vm.Path, out var mapped) && ReferenceEquals(mapped, tvi))
            {
                _treeItemMap.Remove(vm.Path);
            }

            return;
        }

        // DataContextが既に外れている場合は、値側から一致するエントリを探して除去する
        foreach (var pair in _treeItemMap)
        {
            if (ReferenceEquals(pair.Value, tvi))
            {
                _treeItemMap.Remove(pair.Key);
                return;
            }
        }
    }

    private bool _restartFullScanRequested;

    // 設定画面を開き、保存された場合は設定を適用してタイマー・ツリー選択を再構成する
    private async void OpenSettingsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        await ShowSettingsDialogAsync(startOnSubscriptionPage: false);
    }

    private async Task ShowSettingsDialogAsync(bool startOnSubscriptionPage)
    {
        // 設定画面には最新の並び順を渡したいので、ヘッダーのドラッグで変わっている可能性のある
        // 現在の列レイアウトを先に確定させる
        SaveFileListColumnLayout();

        var dialog = new SettingsWindow(
            _viewModel.GetConfiguredRootPaths(),
            _viewModel.GetExcludedPaths(),
            _viewModel.GetFullScanIntervalHours(),
            _viewModel.GetVisibleColumns(),
            _viewModel.GetColumnOrder(),
            _viewModel.GetVisibleTreeNodes(),
            _viewModel.GetTreeNodeOrder(),
            _viewModel.GetTheme(),
            _viewModel.ApplyTheme,
            _viewModel.GetLanguage(),
            _viewModel.ApplyLanguage,
            _storeLicenseService,
            startOnSubscriptionPage)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
        {
            // Cancelで閉じてもダイアログ内でPlusを購読した可能性があるため、Plus機能の表示は反映し直す
            ApplyFileListColumnVisibility();
            ApplyFileListColumnLayout();
            ApplyPlusTreeNodes();
            return;
        }

        // 設定画面で並び順を変える前に、ヘッダーのドラッグで変わっている可能性のある現在の並び順を確定させる
        _viewModel.ApplySettings(
            dialog.ResultRootPaths,
            dialog.ResultExcludedPaths,
            dialog.ResultFullScanIntervalHours,
            dialog.ResultVisibleColumns,
            dialog.ResultColumnOrder,
            dialog.ResultVisibleTreeNodes,
            dialog.ResultTreeNodeOrder);
        // 保存済み幅を消してから並び順・列幅を反映し直す（消し忘れると直後のApplyで元の幅に戻ってしまう）
        if (dialog.ShouldResetColumnWidths)
        {
            ResetFileListColumnWidths();
        }

        ApplyFileListColumnVisibility();
        ApplyFileListColumnLayout();
        ApplyPlusTreeNodes();
        ConfigureScheduledFullScanTimer();
        SyncTreeSelectionToCurrentPath();

        if (dialog.ShouldRunFullScan)
        {
            if (_isFullScanRunning)
            {
                // 今のフルスキャンをキャンセルして再実行する
                _restartFullScanRequested = true;
                _fullScanCts?.Cancel();
            }
            else
            {
                await RunFullScanFromSettingsAsync();
            }
        }
    }

    // 使い方ガイド（GitHub Pages）を既定のブラウザーで開く
    private void OpenUserGuideMenuItem_Click(object sender, RoutedEventArgs e)
    {
        // アプリの表示言語（CurrentUICultureはAppLanguage.Applyが設定済み）に合わせてページを選ぶ
        // （ページ側にも言語の切り替えリンクがあるため、外した場合も辿り着ける）
        var url = string.Equals(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, "ja", StringComparison.OrdinalIgnoreCase)
            ? "https://msmsrep.github.io/ParallelScope/index.ja.html"
            : "https://msmsrep.github.io/ParallelScope/";

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(UiText.Format("UserGuide.OpenFailed", ex.Message), UiText.Get("UserGuide.Caption"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // 表示中のファイル一覧（検索結果・All Files表示・通常一覧のいずれも、ソート順と表示列のまま）をCSVへ書き出す。
    // Plus機能のため、未購読の場合は購読案内を表示して終了する
    private async void ExportCsvMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (!_storeLicenseService.IsPlusActive)
        {
            var answer = MessageBox.Show(
                UiText.Get("Csv.PlusRequired"),
                UiText.Get("Csv.Caption"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (answer == MessageBoxResult.Yes)
            {
                await ShowSettingsDialogAsync(startOnSubscriptionPage: true);
            }

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

    // ツリーで選択されたフォルダのファイル一覧を読み込む
    private void FolderTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is FolderItemViewModel folderItem)
        {
            _viewModel.LoadFiles(folderItem.Path);
        }
    }

    private async void FolderTreeItem_Expanded(object sender, RoutedEventArgs e)
    {
        if (sender is not TreeViewItem { DataContext: FolderItemViewModel folderItem })
        {
            return;
        }

        // 「最近」「よく使う」は展開のタイミングでだけ並べ直す（移動のたびに並べ替えるとツリーが目の前で動いてしまう）
        switch (VirtualFolders.GetKind(folderItem.Path))
        {
            case VirtualFolderKind.Frequent:
                _viewModel.RefreshFrequentFolders();
                return;
            case VirtualFolderKind.Recent:
                _viewModel.RefreshRecentFolders();
                return;
        }

        // TreeViewItemが展開される時に、子フォルダを遅延読み込み（非同期）
        await folderItem.EnsureLoadedAsync();
    }

    // 右クリックされたTreeViewItemを選択状態にしてからコンテキストメニューを表示する
    private void FolderTreeView_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var treeViewItem = GetAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
        if (treeViewItem is null)
        {
            return;
        }

        // 選択・フォーカスでツリーがスクロールすると、マウスを離す時点のカーソル直下が
        // 別ノード（あるいは余白）になりうる。メニューの対象は押した時点のノードで固定する
        _rightClickedTreeViewItem = treeViewItem;

        treeViewItem.IsSelected = true;
        treeViewItem.Focus();
    }

    // 選択中のフォルダに対するコンテキストメニューを動的に構築して開く
    private void FolderTreeView_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        // 組み立てたメニューは自前で開く。WPFの自動表示に任せると、
        // この時点ではまだ割り当てられていないため「1回目は出ず2回目で出る」ことがある
        e.Handled = true;

        // キーボード（アプリケーションキー）からの表示ではカーソル位置が-1で、直前の右クリック位置は無関係
        var isKeyboardInvoked = e.CursorLeft < 0 && e.CursorTop < 0;
        var treeViewItem = isKeyboardInvoked
            ? GetAncestor<TreeViewItem>(e.OriginalSource as DependencyObject)
            : _rightClickedTreeViewItem;
        _rightClickedTreeViewItem = null;

        if (treeViewItem is not { DataContext: FolderItemViewModel folderItem })
        {
            return;
        }

        // 仮想ノード（Folders / Favorites / Recent / Frequently Used）は実パスを持たず個別スキャンできないため、メニューを表示しない
        if (VirtualFolders.IsVirtual(folderItem.Path))
        {
            treeViewItem.ContextMenu = null;
            return;
        }

        var scanMenuItem = new MenuItem
        {
            Header = UiText.Get("Context.ScanSubtree"),
            DataContext = folderItem,
            IsEnabled = !folderItem.IsScanning
        };
        scanMenuItem.Click += ScanFolderMenuItem_Click;

        var contextMenu = new ContextMenu
        {
            DataContext = folderItem,
            PlacementTarget = treeViewItem
        };
        contextMenu.Items.Add(scanMenuItem);

        // お気に入りはPlus機能のため、未購読の間はメニューにも出さない
        if (_viewModel.ArePlusFeaturesEnabled)
        {
            var isFavorite = _viewModel.IsFavorite(folderItem.Path);
            var favoriteMenuItem = new MenuItem
            {
                Header = UiText.Get(isFavorite ? "Context.RemoveFavorite" : "Context.AddFavorite"),
                DataContext = folderItem
            };
            favoriteMenuItem.Click += ToggleFavoriteMenuItem_Click;

            contextMenu.Items.Add(new Separator());
            contextMenu.Items.Add(favoriteMenuItem);
        }

        if (isKeyboardInvoked)
        {
            contextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        }

        // テーマ・言語のリソースを引けるよう、開く前に論理ツリーへ繋いでおく
        treeViewItem.ContextMenu = contextMenu;
        contextMenu.IsOpen = true;
    }

    // コンテキストメニューから、選択フォルダのお気に入り登録/解除を切り替える
    private void ToggleFavoriteMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: FolderItemViewModel folderItem })
        {
            _viewModel.ToggleFavorite(folderItem.Path);
        }
    }

    // 戻る履歴のフォルダへ移動し、ツリー選択を同期する
    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.GoBack())
        {
            SyncTreeSelectionToCurrentPath();
        }
    }

    // 進む履歴のフォルダへ移動し、ツリー選択を同期する
    private void ForwardButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.GoForward())
        {
            SyncTreeSelectionToCurrentPath();
        }
    }

    // 親フォルダへ移動し、ツリー選択を同期する
    private void UpButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.GoUp())
        {
            SyncTreeSelectionToCurrentPath();
        }
    }

    // Enterキーでアドレス欄のパスへ移動する
    private void AddressTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        NavigateByAddressInput();
        e.Handled = true;
    }

    // コンテキストメニューから、選択フォルダ配下の個別スキャンを実行する
    private async void ScanFolderMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: FolderItemViewModel folderItem })
        {
            return;
        }

        if (folderItem.IsScanning)
        {
            return;
        }

        await RunFolderScanAsync(folderItem);
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

    // 指定アイテムの祖先TreeViewItemをすべて展開する
    private void ExpandParents(TreeViewItem item)
    {
        // 展開するアイテムをすべて収集してからバッチで展開
        var itemsToExpand = new List<TreeViewItem>();
        DependencyObject parent = VisualTreeHelper.GetParent(item);

        while (parent is TreeViewItem parentItem)
        {
            itemsToExpand.Add(parentItem);
            parent = VisualTreeHelper.GetParent(parentItem);
        }

        // バッチ展開（複数の IsExpanded 設定をまとめる）
        foreach (var parentItem in itemsToExpand)
        {
            parentItem.IsExpanded = true;
        }

        // 最後に一度だけレイアウト更新
        if (itemsToExpand.Count > 0)
        {
            item.UpdateLayout();
        }
    }

    // フォルダツリーの選択状態を現在のパスに同期する（必要に応じて祖先ノードを遅延展開）
    private void SyncTreeSelectionToCurrentPath()
    {
        var path = PathNormalizer.Normalize(_viewModel.CurrentPath);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (_treeItemMap.TryGetValue(path, out var tvi))
        {
            ExpandParents(tvi);
            tvi.IsSelected = true;
            tvi.BringIntoView();
            return;
        }

        var rootFolder = _viewModel.RootFolders.FirstOrDefault(root => PathNormalizer.IsAncestorOrSame(root.Path, path));
        if (rootFolder is null)
        {
            return;
        }

        var normalizedRootPath = PathNormalizer.Normalize(rootFolder.Path);
        var relativePath = path.StartsWith(normalizedRootPath, StringComparison.OrdinalIgnoreCase)
            ? path[normalizedRootPath.Length..]
            : string.Empty;

        var pathComponents = relativePath
            .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries)
            .ToList();

        // ルートは仮想「Folders」ノードの子になったため、そのTreeViewItemを展開してから配下を辿る
        // （Favorites/Frequently Usedが上に挿入されうるので、インデックスではなくノード実体から引く）
        if (FolderTreeView.ItemContainerGenerator.ContainerFromItem(_viewModel.AllRootsNode) is not TreeViewItem allRootsItem)
        {
            return;
        }

        allRootsItem.IsExpanded = true;

        ExpandAndSelectByPath(allRootsItem, rootFolder, pathComponents, 0);
        if (_treeItemMap.TryGetValue(path, out tvi))
        {
            ExpandParents(tvi);
        }
    }
    // パス構成要素を1つずつ辿りながらツリーを再帰的に展開し、目的のノードを選択する
    private bool ExpandAndSelectByPath(ItemsControl parentControl, FolderItemViewModel folderItem,
        List<string> pathComponents, int componentIndex)
    {
        // 初回呼び出しのみレイアウト更新を行う
        if (componentIndex == 0)
        {
            parentControl.UpdateLayout();
        }

        if (parentControl.ItemContainerGenerator.ContainerFromItem(folderItem) is not TreeViewItem treeViewItem)
        {
            return false;
        }

        // ターゲットに到達した
        if (componentIndex >= pathComponents.Count)
        {
            treeViewItem.IsSelected = true;
            treeViewItem.BringIntoView();
            // 最終更新のみ一度実行
            treeViewItem.UpdateLayout();
            return true;
        }

        // 遅延読み込みを実行（次のディレクトリを探すために）
        folderItem.EnsureLoaded();
        treeViewItem.IsExpanded = true;
        // 中間のUpdateLayout()は削除（最後の更新のみで十分）

        // 次のディレクトリ成分を探す
        var nextComponent = pathComponents[componentIndex];
        var matchingChild = folderItem.SubFolders.FirstOrDefault(child =>
            string.Equals(child.DisplayName, nextComponent, StringComparison.OrdinalIgnoreCase));

        if (matchingChild is not null)
        {
            return ExpandAndSelectByPath(treeViewItem, matchingChild, pathComponents, componentIndex + 1);
        }

        return false;
    }
    // 指定した要素の祖先から、型Tに一致する最初の要素を探す（コンテキストメニュー表示位置の特定などに使用）
    private static T? GetAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    // アドレス欄のパスへ移動する。失敗した場合はエラーメッセージを表示する
    private void NavigateByAddressInput()
    {
        if (_viewModel.TryNavigateByAddressInput())
        {
            SyncTreeSelectionToCurrentPath();
            return;
        }

        MessageBox.Show(UiText.Get("Navigation.Failed"), UiText.Get("Navigation.Caption"), MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    // 設定画面の「保存してフルスキャン」から呼ばれる、完了メッセージ付きのフルスキャン
    private async Task RunFullScanFromSettingsAsync()
    {
        await RunFullScanAsync(showCompletionMessage: true, useWaitCursor: true);
    }

    // 選択フォルダ配下をスキャンし、現在表示中のフォルダに影響する場合は一覧を再読込する
    private async Task RunFolderScanAsync(FolderItemViewModel folderItem)
    {
        folderItem.IsScanning = true;

        try
        {
            var scannedFolderCount = await _viewModel.ScanFolderSubtreeAsync(folderItem.Path);

            if (PathNormalizer.IsAncestorOrSame(folderItem.Path, _viewModel.CurrentPath))
            {
                // LoadFiles(CurrentPath) は NavigateTo の同一パス早期returnで何もしないため、再読み込み専用APIを使う
                _viewModel.RefreshCurrentFolder();
                SyncTreeSelectionToCurrentPath();
            }

            MessageBox.Show(
                UiText.Format("Scan.Folder.Completed", scannedFolderCount),
                UiText.Get("Scan.Folder.Caption"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(UiText.Format("Scan.Folder.Failed", ex.Message), UiText.Get("Scan.Folder.ErrorCaption"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            folderItem.IsScanning = false;
        }
    }

    // 定期スキャンタイマー発火時に自動フルスキャンを実行する
    private async void ScheduledFullScanTimer_Tick(object? sender, EventArgs e)
    {
        await RunAutomaticFullScanAsync();
    }

    // 設定されたフルスキャン間隔でタイマーを再構成する
    private void ConfigureScheduledFullScanTimer()
    {
        _scheduledFullScanTimer.Stop();
        _scheduledFullScanTimer.Interval = TimeSpan.FromHours(_viewModel.GetFullScanIntervalHours());
        _scheduledFullScanTimer.Start();
    }

    // 完了メッセージを表示しない、バックグラウンド用のフルスキャン
    private Task RunAutomaticFullScanAsync()
    {
        return RunFullScanAsync(showCompletionMessage: false, useWaitCursor: false);
    }
    private CancellationTokenSource? _fullScanCts;
    // フルスキャン本体。多重実行を防止し、完了/キャンセル/失敗に応じてメッセージを出し分ける
    private async Task RunFullScanAsync(bool showCompletionMessage, bool useWaitCursor)
    {
        if (_isFullScanRunning)
        {
            return;
        }

        _isFullScanRunning = true;
        _fullScanCts = new CancellationTokenSource();
        var token = _fullScanCts.Token;
        SetRootScanningState(true);

        try
        {
            var scannedFolderCount = await _viewModel.FullScanConfiguredRootsAsync(token);

            if (!string.IsNullOrWhiteSpace(_viewModel.CurrentPath))
            {
                // LoadFiles(CurrentPath) は NavigateTo の同一パス早期returnで何もしないため、再読み込み専用APIを使う
                _viewModel.RefreshCurrentFolder();
                SyncTreeSelectionToCurrentPath();
            }

            if (showCompletionMessage)
            {
                MessageBox.Show(
                    UiText.Format("Scan.Full.Completed", scannedFolderCount),
                    UiText.Get("Scan.Full.Caption"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (OperationCanceledException)
        {
            if (showCompletionMessage)
            {
                MessageBox.Show(UiText.Get("Scan.Full.Canceled"), UiText.Get("Scan.Full.CanceledCaption"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            if (showCompletionMessage)
            {
                MessageBox.Show(UiText.Format("Scan.Full.Failed", ex.Message), UiText.Get("Scan.Full.ErrorCaption"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        finally
        {
            SetRootScanningState(false);
            _isFullScanRunning = false;
            _fullScanCts?.Dispose();
            _fullScanCts = null;

            // キャンセル後に再実行要求があれば、ここで新しいフルスキャンを開始
            if (_restartFullScanRequested)
            {
                _restartFullScanRequested = false;
                await RunFullScanAsync(showCompletionMessage, useWaitCursor);
            }
        }
    }
    // キャンセルボタンなどから呼ぶ
    private void CancelFullScan()
    {
        _fullScanCts?.Cancel();
    }
    // 全ルートフォルダのスキャン中表示フラグを一括で切り替える
    private void SetRootScanningState(bool isScanning)
    {
        foreach (var rootFolder in _viewModel.RootFolders)
        {
            rootFolder.IsScanning = isScanning;
        }
    }
}