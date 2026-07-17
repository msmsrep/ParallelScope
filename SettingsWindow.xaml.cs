using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using ParallelScope.Utilities;

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

    // 現在の設定値でダイアログの初期状態を構築する
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

    // 入力欄のルートパスを検証・正規化して一覧へ追加する
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
            normalized = PathNormalizer.Normalize(input);
        }
        catch
        {
            MessageBox.Show("The path format is invalid.", "Input Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // 応答の遅いNASパスでも設定画面を固めない（タイムアウト時は存在する扱いで受け付ける）
        if (!DirectoryAvailabilityChecker.ExistsOrTimedOut(normalized))
        {
            MessageBox.Show("The specified folder does not exist.", "Input Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_rootPaths.Any(x => string.Equals(PathNormalizer.Normalize(x), normalized, StringComparison.OrdinalIgnoreCase)))
        {
            NewRootPathTextBox.Clear();
            return;
        }

        _rootPaths.Add(normalized);
        NewRootPathTextBox.Clear();
    }

    // 選択中のルートパスを一覧から削除する
    private void RemoveRootPathButton_Click(object sender, RoutedEventArgs e)
    {
        if (RootPathsListBox.SelectedItem is not string selectedPath)
        {
            return;
        }

        _rootPaths.Remove(selectedPath);
    }

    // 入力欄の除外パスを検証・正規化して一覧へ追加する
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
            normalized = PathNormalizer.Normalize(input);
        }
        catch
        {
            MessageBox.Show("The path format is invalid.", "Input Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // 応答の遅いNASパスでも設定画面を固めない（タイムアウト時は存在する扱いで受け付ける）
        if (!DirectoryAvailabilityChecker.ExistsOrTimedOut(normalized))
        {
            MessageBox.Show("The specified folder does not exist.", "Input Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_excludedPaths.Any(x => string.Equals(PathNormalizer.Normalize(x), normalized, StringComparison.OrdinalIgnoreCase)))
        {
            NewExcludedPathTextBox.Clear();
            return;
        }

        _excludedPaths.Add(normalized);
        NewExcludedPathTextBox.Clear();
    }

    // 選択中の除外パスを一覧から削除する
    private void RemoveExcludedPathButton_Click(object sender, RoutedEventArgs e)
    {
        if (ExcludedPathsListBox.SelectedItem is not string selectedPath)
        {
            return;
        }

        _excludedPaths.Remove(selectedPath);
    }

    // 入力内容を検証し、フルスキャンを行わずにダイアログを閉じる
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

    // 入力内容を検証し、保存後にフルスキャンを行う指示付きでダイアログを閉じる
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

    // 保存可能な入力内容か検証する（ルートフォルダが1つ以上、間隔が正の整数）
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

    // 未設定/不正値の場合はデフォルトのフルスキャン間隔にフォールバックする
    private static int NormalizeFullScanIntervalHours(int hours)
    {
        return hours > 0 ? hours : Data.AppSettings.DefaultFullScanIntervalHours;
    }
}
