using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using ParallelScope.Services;
using ParallelScope.Utilities;

namespace ParallelScope;

public partial class SettingsWindow : Window
{
    private readonly ObservableCollection<string> _rootPaths;
    private readonly ObservableCollection<string> _excludedPaths;
    private readonly Dictionary<string, CheckBox> _columnCheckBoxes;
    private readonly StoreLicenseService _storeLicenseService;
    private int _fullScanIntervalHours;

    public IReadOnlyList<string> ResultRootPaths => _rootPaths.ToList();
    public IReadOnlyList<string> ResultExcludedPaths => _excludedPaths.ToList();
    public int ResultFullScanIntervalHours => _fullScanIntervalHours;
    public bool ShouldRunFullScan { get; private set; }

    /// <summary>チェックされた表示列のキー一覧（画面上の列順）。</summary>
    public IReadOnlyList<string> ResultVisibleColumns =>
        FileListColumns.OptionalColumns
            .Where(column => _columnCheckBoxes[column].IsChecked == true)
            .ToList();

    // 現在の設定値でダイアログの初期状態を構築する
    public SettingsWindow(
        IEnumerable<string> currentRootPaths,
        IEnumerable<string> currentExcludedPaths,
        int currentFullScanIntervalHours,
        IEnumerable<string> currentVisibleColumns,
        StoreLicenseService storeLicenseService)
    {
        InitializeComponent();

        _storeLicenseService = storeLicenseService;
        ApplyPlusLicenseState();

        _rootPaths = new ObservableCollection<string>(currentRootPaths);
        _excludedPaths = new ObservableCollection<string>(currentExcludedPaths);
        _fullScanIntervalHours = NormalizeFullScanIntervalHours(currentFullScanIntervalHours);
        RootPathsListBox.ItemsSource = _rootPaths;
        ExcludedPathsListBox.ItemsSource = _excludedPaths;
        FullScanIntervalHoursTextBox.Text = _fullScanIntervalHours.ToString();

        _columnCheckBoxes = new Dictionary<string, CheckBox>(StringComparer.OrdinalIgnoreCase)
        {
            [FileListColumns.Location] = LocationColumnCheckBox,
            [FileListColumns.Type] = TypeColumnCheckBox,
            [FileListColumns.Size] = SizeColumnCheckBox,
            [FileListColumns.Modified] = ModifiedColumnCheckBox,
            [FileListColumns.Created] = CreatedColumnCheckBox,
            [FileListColumns.Attributes] = AttributesColumnCheckBox
        };

        var visibleColumnSet = currentVisibleColumns.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (column, checkBox) in _columnCheckBoxes)
        {
            checkBox.IsChecked = visibleColumnSet.Contains(column);
        }
    }

    // Plusの購読状態をDisplay Columns/Subscriptionページへ反映する。
    // 未購読時はチェックボックス群を無効化（WPF標準の無効化スタイルで薄字・操作不可になる）し、アンロック案内を表示する
    private void ApplyPlusLicenseState()
    {
        var isActive = _storeLicenseService.IsPlusActive;
        ColumnCheckBoxesPanel.IsEnabled = isActive;
        PlusUpsellCard.Visibility = isActive ? Visibility.Collapsed : Visibility.Visible;

        // Subscriptionページ: 購読済みなら状態表示のみ、未購読なら購入ボタンを表示する
        PlusActiveTextBlock.Visibility = isActive ? Visibility.Visible : Visibility.Collapsed;
        SubscribePlusButton.Visibility = isActive ? Visibility.Collapsed : Visibility.Visible;

        if (!isActive)
        {
            _ = LoadPlusPriceAsync();
        }
    }

    // ストアから実際の表示価格（通貨ローカライズ済み）を取得してボタンに反映する。取得できなければ汎用表記のまま
    private async Task LoadPlusPriceAsync()
    {
        var price = await _storeLicenseService.GetPlusFormattedPriceAsync();
        if (!string.IsNullOrEmpty(price))
        {
            SubscribePlusButton.Content = $"🔓 Subscribe to Plus ({price} / month)";
        }
    }

    // 購入ダイアログを表示し、購読が成立したらチェックボックス群を有効化する
    private async void SubscribePlusButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_storeLicenseService.IsStoreAvailable)
        {
            PlusStatusTextBlock.Text = "The Microsoft Store is not available. Please install this app from the Microsoft Store to subscribe.";
            PlusStatusTextBlock.Visibility = Visibility.Visible;
            return;
        }

        // 購入ダイアログ表示中の多重クリックを防ぐ
        SubscribePlusButton.IsEnabled = false;
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var purchased = await _storeLicenseService.PurchasePlusAsync(hwnd);
            if (purchased)
            {
                ApplyPlusLicenseState();
            }
            else
            {
                PlusStatusTextBlock.Text = "The purchase was not completed.";
                PlusStatusTextBlock.Visibility = Visibility.Visible;
            }
        }
        finally
        {
            SubscribePlusButton.IsEnabled = true;
        }
    }

    private readonly string _kofiUrl = "https://ko-fi.com/msmsrep";
    private readonly string _gitHubSponsorsUrl = "https://github.com/sponsors/msmsrep";
    // Microsoft Storeのサブスクリプションはアプリ内から解約できないため、Microsoftアカウントの管理ページへ誘導する
    private readonly string _manageSubscriptionUrl = "https://account.microsoft.com/services";

    // 左メニューの選択に応じて右側の設定ページを切り替える
    private void SettingsMenuListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // InitializeComponent中（初期選択の適用時）はパネルがまだ生成されていない
        if (RootSettingsPanel is null || ColumnSettingsPanel is null || SubscriptionPanel is null || SupportPanel is null)
        {
            return;
        }

        var showColumns = SettingsMenuListBox.SelectedIndex == 1;
        var showSubscription = SettingsMenuListBox.SelectedIndex == 2;
        var showSupport = SettingsMenuListBox.SelectedIndex == 3;
        var showRoot = !showColumns && !showSubscription && !showSupport;
        RootSettingsPanel.Visibility = showRoot ? Visibility.Visible : Visibility.Collapsed;
        ColumnSettingsPanel.Visibility = showColumns ? Visibility.Visible : Visibility.Collapsed;
        SubscriptionPanel.Visibility = showSubscription ? Visibility.Visible : Visibility.Collapsed;
        SupportPanel.Visibility = showSupport ? Visibility.Visible : Visibility.Collapsed;
        SaveAndFullScanButton.Visibility = showRoot ? Visibility.Visible : Visibility.Collapsed;
        // Subscription/Supportページには保存対象の設定がないため、Save/Cancelボタンも非表示にする
        SaveButton.Visibility = showSubscription || showSupport ? Visibility.Collapsed : Visibility.Visible;
        CancelButton.Visibility = showSubscription || showSupport ? Visibility.Collapsed : Visibility.Visible;
    }

    // Display ColumnsページのアンロックからSubscriptionページへ遷移する
    private void GoToSubscriptionButton_Click(object sender, RoutedEventArgs e)
    {
        SettingsMenuListBox.SelectedIndex = 2;
    }

    // Microsoftアカウントのサブスクリプション管理（解約）ページをブラウザで開く
    private void ManageSubscriptionButton_Click(object sender, RoutedEventArgs e)
    {
        OpenSupportUrl(_manageSubscriptionUrl, "Manage subscription");
    }

    // 開発者への寄付ページ（Ko-fi）をブラウザで開く
    private void KofiButton_Click(object sender, RoutedEventArgs e)
    {
        OpenSupportUrl(_kofiUrl, "Ko-fi");
    }

    // GitHub Sponsorsページをブラウザで開く
    private void GitHubSponsorsButton_Click(object sender, RoutedEventArgs e)
    {
        OpenSupportUrl(_gitHubSponsorsUrl, "GitHub Sponsors");
    }

    private void OpenSupportUrl(string url, string caption)
    {
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
            MessageBox.Show(this, ex.Message, caption, MessageBoxButton.OK, MessageBoxImage.Error);
        }
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

    // 選択中のルートパスを1つ上へ移動する
    private void MoveRootPathUpButton_Click(object sender, RoutedEventArgs e)
    {
        MoveSelectedRootPath(-1);
    }

    // 選択中のルートパスを1つ下へ移動する
    private void MoveRootPathDownButton_Click(object sender, RoutedEventArgs e)
    {
        MoveSelectedRootPath(1);
    }

    private void MoveSelectedRootPath(int offset)
    {
        var index = RootPathsListBox.SelectedIndex;
        var newIndex = index + offset;
        if (index < 0 || newIndex < 0 || newIndex >= _rootPaths.Count)
        {
            return;
        }

        _rootPaths.Move(index, newIndex);
        RootPathsListBox.SelectedIndex = newIndex;
        RootPathsListBox.ScrollIntoView(RootPathsListBox.SelectedItem);
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
