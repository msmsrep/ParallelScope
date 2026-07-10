using System.Windows;
using System.Windows.Controls;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ParallelScope.Utilities;
using ParallelScope.ViewModels;

namespace ParallelScope;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly DispatcherTimer _scheduledFullScanTimer;
    private bool _hasStartedAutomaticFullScan;
    private bool _isFullScanRunning;
    private readonly string _kofiUrl = "https://ko-fi.com/msmsrep";
    private readonly string _gitHubSponsorsUrl = "https://github.com/sponsors/msmsrep";
    public MainWindow()
    {
        InitializeComponent();

        // AppxManifest.xmlのバージョンをタイトルに付与する（取得できない場合は元のタイトルのまま）
        Title = BuildWindowTitleWithVersion(Title);

        _viewModel = new MainWindowViewModel();
        _scheduledFullScanTimer = new DispatcherTimer();
        _scheduledFullScanTimer.Tick += ScheduledFullScanTimer_Tick;
        DataContext = _viewModel;
        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;

        SyncTreeSelectionToCurrentPath();
    }

    // ウィンドウ表示後に自動フルスキャンを1回だけ実行し、以降は定期スキャンタイマーに切り替える
    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_hasStartedAutomaticFullScan)
        {
            return;
        }

        _hasStartedAutomaticFullScan = true;
        await RunAutomaticFullScanAsync();
        ConfigureScheduledFullScanTimer();
    }

    // ウィンドウクローズ時に定期スキャンタイマーを停止する
    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _scheduledFullScanTimer.Stop();
        _scheduledFullScanTimer.Tick -= ScheduledFullScanTimer_Tick;
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
        if (sender is TreeViewItem tvi && tvi.DataContext is FolderItemViewModel vm)
        {
            _treeItemMap[vm.Path] = tvi;
        }
    }

    private bool _restartFullScanRequested;

    private void KofiMenuItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _kofiUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Ko-fi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void GitHubSponsorsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _gitHubSponsorsUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "GitHub Sponsors", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // 設定画面を開き、保存された場合は設定を適用してタイマー・ツリー選択を再構成する
    private async void OpenSettingsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(
            _viewModel.GetConfiguredRootPaths(),
            _viewModel.GetExcludedPaths(),
            _viewModel.GetFullScanIntervalHours())
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        _viewModel.ApplySettings(dialog.ResultRootPaths, dialog.ResultExcludedPaths, dialog.ResultFullScanIntervalHours);
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

        treeViewItem.IsSelected = true;
        treeViewItem.Focus();
    }

    // 選択中のフォルダに対する「配下を全てスキャン」メニューを動的に構築する
    private void FolderTreeItem_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not TreeViewItem { DataContext: FolderItemViewModel folderItem } treeViewItem)
        {
            return;
        }

        var sourceTreeViewItem = GetAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
        if (!ReferenceEquals(sourceTreeViewItem, treeViewItem))
        {
            return;
        }

        var menuItem = new MenuItem
        {
            Header = "Scan everything under this folder",
            DataContext = folderItem,
            IsEnabled = !folderItem.IsScanning
        };
        menuItem.Click += ScanFolderMenuItem_Click;

        treeViewItem.ContextMenu = new ContextMenu
        {
            DataContext = folderItem
        };
        treeViewItem.ContextMenu.Items.Add(menuItem);
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
            MessageBox.Show($"Could not open the file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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

        ExpandAndSelectByPath(FolderTreeView, rootFolder, pathComponents, 0);
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

        MessageBox.Show("Could not navigate to the specified folder. Please check the path.", "Navigation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                _viewModel.LoadFiles(_viewModel.CurrentPath);
                SyncTreeSelectionToCurrentPath();
            }

            MessageBox.Show(
                $"Scan completed. Updated cache for {scannedFolderCount} folder(s).",
                "Folder Scan",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Scan failed: {ex.Message}", "Folder Scan Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                _viewModel.LoadFiles(_viewModel.CurrentPath);
                SyncTreeSelectionToCurrentPath();
            }

            if (showCompletionMessage)
            {
                MessageBox.Show(
                    $"Full scan completed. Updated cache for {scannedFolderCount} folder(s).",
                    "Full Scan",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (OperationCanceledException)
        {
            if (showCompletionMessage)
            {
                MessageBox.Show("Full scan was canceled.", "Full Scan Canceled", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            if (showCompletionMessage)
            {
                MessageBox.Show($"Full scan failed: {ex.Message}", "Full Scan Error", MessageBoxButton.OK, MessageBoxImage.Error);
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