using System.Collections.ObjectModel;
using System.IO;
using System.Windows;

namespace ParallelScope;

public partial class SettingsWindow : Window
{
    private readonly ObservableCollection<string> _rootPaths;
    private readonly ObservableCollection<string> _excludedPaths;
    private int _fullScanIntervalHours;

    public IReadOnlyList<string> ResultRootPaths => _rootPaths.ToList();
    public IReadOnlyList<string> ResultExcludedPaths => _excludedPaths.ToList();
    public int ResultFullScanIntervalHours => _fullScanIntervalHours;
    public bool ShouldRunFullScan { get; private set; }

    public SettingsWindow(IEnumerable<string> currentRootPaths, IEnumerable<string> currentExcludedPaths, int currentFullScanIntervalHours)
    {
        InitializeComponent();

        _rootPaths = new ObservableCollection<string>(currentRootPaths);
        _excludedPaths = new ObservableCollection<string>(currentExcludedPaths);
        _fullScanIntervalHours = NormalizeFullScanIntervalHours(currentFullScanIntervalHours);
        RootPathsListBox.ItemsSource = _rootPaths;
        ExcludedPathsListBox.ItemsSource = _excludedPaths;
        FullScanIntervalHoursTextBox.Text = _fullScanIntervalHours.ToString();
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
            MessageBox.Show("The path format is invalid.", "Input Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!Directory.Exists(normalized))
        {
            MessageBox.Show("The specified folder does not exist.", "Input Error", MessageBoxButton.OK, MessageBoxImage.Warning);
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

    private void AddExcludedPathButton_Click(object sender, RoutedEventArgs e)
    {
        var input = NewExcludedPathTextBox.Text?.Trim() ?? string.Empty;
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
            MessageBox.Show("The path format is invalid.", "Input Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!Directory.Exists(normalized))
        {
            MessageBox.Show("The specified folder does not exist.", "Input Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_excludedPaths.Any(x => string.Equals(NormalizePath(x), normalized, StringComparison.OrdinalIgnoreCase)))
        {
            NewExcludedPathTextBox.Clear();
            return;
        }

        _excludedPaths.Add(normalized);
        NewExcludedPathTextBox.Clear();
    }

    private void RemoveExcludedPathButton_Click(object sender, RoutedEventArgs e)
    {
        if (ExcludedPathsListBox.SelectedItem is not string selectedPath)
        {
            return;
        }

        _excludedPaths.Remove(selectedPath);
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanSave())
        {
            return;
        }

        ShouldRunFullScan = false;
        DialogResult = true;
        Close();
    }

    private void SaveAndFullScanButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanSave())
        {
            return;
        }

        ShouldRunFullScan = true;
        DialogResult = true;
        Close();
    }

    private bool CanSave()
    {
        if (_rootPaths.Count == 0)
        {
            MessageBox.Show("Please add at least one target root folder.", "Save Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (!int.TryParse(FullScanIntervalHoursTextBox.Text?.Trim(), out var parsedHours) || parsedHours <= 0)
        {
            MessageBox.Show("Enter the auto full scan interval as a positive number of hours.", "Save Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        _fullScanIntervalHours = parsedHours;
        return true;
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

    private static int NormalizeFullScanIntervalHours(int hours)
    {
        return hours > 0 ? hours : Data.AppSettings.DefaultFullScanIntervalHours;
    }
}
