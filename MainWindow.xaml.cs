using System.Windows;
using System.Windows.Controls;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using ParallelScope.ViewModels;

namespace ParallelScope;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private MainWindowViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();

        _viewModel = new MainWindowViewModel();
        DataContext = _viewModel;

        SyncTreeSelectionToCurrentPath();
    }

    private void OpenSettingsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(_viewModel.GetConfiguredRootPaths())
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        _viewModel.ApplyRootPaths(dialog.ResultRootPaths);
        SyncTreeSelectionToCurrentPath();
    }

    private void FolderTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is FolderItemViewModel folderItem)
        {
            _viewModel.LoadFiles(folderItem.Path);
        }
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
}