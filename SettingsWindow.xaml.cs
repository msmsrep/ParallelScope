using System.Collections.ObjectModel;
using System.IO;
using System.Windows;

namespace ParallelScope;

public partial class SettingsWindow : Window
{
    private readonly ObservableCollection<string> _rootPaths;

    public IReadOnlyList<string> ResultRootPaths => _rootPaths.ToList();

    public SettingsWindow(IEnumerable<string> currentRootPaths)
    {
        InitializeComponent();

        _rootPaths = new ObservableCollection<string>(currentRootPaths);
        RootPathsListBox.ItemsSource = _rootPaths;
    }

    private void AddRootPathButton_Click(object sender, RoutedEventArgs e)
    {
        var input = NewRootPathTextBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(input))
        {
            return;
        }

        string normalized;
        try
        {
            normalized = NormalizePath(input);
        }
        catch
        {
            MessageBox.Show("パスの形式が正しくありません。", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!Directory.Exists(normalized))
        {
            MessageBox.Show("指定されたフォルダが存在しません。", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_rootPaths.Any(x => string.Equals(NormalizePath(x), normalized, StringComparison.OrdinalIgnoreCase)))
        {
            NewRootPathTextBox.Clear();
            return;
        }

        _rootPaths.Add(normalized);
        NewRootPathTextBox.Clear();
    }

    private void RemoveRootPathButton_Click(object sender, RoutedEventArgs e)
    {
        if (RootPathsListBox.SelectedItem is not string selectedPath)
        {
            return;
        }

        _rootPaths.Remove(selectedPath);
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_rootPaths.Count == 0)
        {
            MessageBox.Show("対象ルートを1件以上追加してください。", "保存エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
        Close();
    }

    private static string NormalizePath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var rootPath = Path.GetPathRoot(fullPath);

        if (!string.IsNullOrEmpty(rootPath) && string.Equals(fullPath, rootPath, StringComparison.OrdinalIgnoreCase))
        {
            return fullPath;
        }

        return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
