using System.Windows;
using System.Windows.Controls;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
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

    public MainWindow()
    {
        InitializeComponent();

        _viewModel = new MainWindowViewModel();
        _scheduledFullScanTimer = new DispatcherTimer();
        _scheduledFullScanTimer.Tick += ScheduledFullScanTimer_Tick;
        DataContext = _viewModel;
        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;

        SyncTreeSelectionToCurrentPath();
    }

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

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _scheduledFullScanTimer.Stop();
        _scheduledFullScanTimer.Tick -= ScheduledFullScanTimer_Tick;
    }
    private bool _restartFullScanRequested;
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

    private void FolderTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is FolderItemViewModel folderItem)
        {
            _viewModel.LoadFiles(folderItem.Path);
        }
    }

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

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.GoBack())
        {
            SyncTreeSelectionToCurrentPath();
        }
    }

    private void ForwardButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.GoForward())
        {
            SyncTreeSelectionToCurrentPath();
        }
    }

    private void UpButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.GoUp())
        {
            SyncTreeSelectionToCurrentPath();
        }
    }

    private void AddressTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        NavigateByAddressInput();
        e.Handled = true;
    }

    private void SearchTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        ExecuteSearch();
        e.Handled = true;
    }

    private void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        ExecuteSearch();
    }

    private void ClearSearchButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.SearchQuery = string.Empty;
        _viewModel.ClearSearch();
    }

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

    private void SyncTreeSelectionToCurrentPath()
    {
        var targetPath = _viewModel.CurrentPath;
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            return;
        }

        foreach (var root in _viewModel.RootFolders)
        {
            if (!IsAncestorOrSamePath(root.Path, targetPath))
            {
                continue;
            }

            if (ExpandAndSelect(FolderTreeView, root, targetPath))
            {
                return;
            }
        }
    }

    private bool ExpandAndSelect(ItemsControl parentControl, FolderItemViewModel folderItem, string targetPath)
    {
        parentControl.UpdateLayout();
        if (parentControl.ItemContainerGenerator.ContainerFromItem(folderItem) is not TreeViewItem treeViewItem)
        {
            return false;
        }

        if (IsSamePath(folderItem.Path, targetPath))
        {
            treeViewItem.IsSelected = true;
            treeViewItem.BringIntoView();
            return true;
        }

        if (!IsAncestorOrSamePath(folderItem.Path, targetPath))
        {
            return false;
        }

        treeViewItem.IsExpanded = true;
        treeViewItem.UpdateLayout();

        foreach (var child in folderItem.SubFolders)
        {
            if (ExpandAndSelect(treeViewItem, child, targetPath))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSamePath(string leftPath, string rightPath)
    {
        return string.Equals(NormalizePath(leftPath), NormalizePath(rightPath), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAncestorOrSamePath(string ancestorPath, string targetPath)
    {
        var normalizedAncestor = NormalizePath(ancestorPath);
        var normalizedTarget = NormalizePath(targetPath);

        if (string.Equals(normalizedAncestor, normalizedTarget, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var prefix = normalizedAncestor.EndsWith(Path.DirectorySeparatorChar)
            ? normalizedAncestor
            : normalizedAncestor + Path.DirectorySeparatorChar;

        return normalizedTarget.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var fullPath = Path.GetFullPath(path);
        var rootPath = Path.GetPathRoot(fullPath);

        if (!string.IsNullOrEmpty(rootPath) && string.Equals(fullPath, rootPath, StringComparison.OrdinalIgnoreCase))
        {
            return fullPath;
        }

        return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

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

    private void NavigateByAddressInput()
    {
        if (_viewModel.TryNavigateByAddressInput())
        {
            SyncTreeSelectionToCurrentPath();
            return;
        }

        MessageBox.Show("Could not navigate to the specified folder. Please check the path.", "Navigation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void ExecuteSearch()
    {
        if (_viewModel.SearchCurrentPath())
        {
            return;
        }

        MessageBox.Show("Please navigate to a searchable folder before running a search.", "Search Error", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private async Task RunFullScanFromSettingsAsync()
    {
        await RunFullScanAsync(showCompletionMessage: true, useWaitCursor: true);
    }

    private async Task RunFolderScanAsync(FolderItemViewModel folderItem)
    {
        folderItem.IsScanning = true;

        try
        {
            var scannedFolderCount = await _viewModel.ScanFolderSubtreeAsync(folderItem.Path);

            if (IsAncestorOrSamePath(folderItem.Path, _viewModel.CurrentPath))
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

    private async void ScheduledFullScanTimer_Tick(object? sender, EventArgs e)
    {
        await RunAutomaticFullScanAsync();
    }

    private void ConfigureScheduledFullScanTimer()
    {
        _scheduledFullScanTimer.Stop();
        _scheduledFullScanTimer.Interval = TimeSpan.FromHours(_viewModel.GetFullScanIntervalHours());
        _scheduledFullScanTimer.Start();
    }

    private Task RunAutomaticFullScanAsync()
    {
        return RunFullScanAsync(showCompletionMessage: false, useWaitCursor: false);
    }
    private CancellationTokenSource? _fullScanCts;
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
    private void SetRootScanningState(bool isScanning)
    {
        foreach (var rootFolder in _viewModel.RootFolders)
        {
            rootFolder.IsScanning = isScanning;
        }
    }
}