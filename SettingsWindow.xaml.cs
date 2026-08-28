using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using ParallelScope.Services;
using ParallelScope.Utilities;
using ParallelScope.ViewModels;

namespace ParallelScope;

public partial class SettingsWindow : Window
{
    private readonly ObservableCollection<string> _rootPaths;
    private readonly ObservableCollection<string> _excludedPaths;
    // ファイル一覧の列（Nameを含む）。一覧の並びがそのまま列の並び順になる
    private readonly ObservableCollection<ColumnOptionViewModel> _columnOptions;
    // ツリー最上位のノード（常に表示のFoldersを含む）。一覧の並びがそのままツリーの並び順になる
    private readonly ObservableCollection<ColumnOptionViewModel> _treeNodeOptions;
    private readonly StoreLicenseService _storeLicenseService;
    // テーマ・言語はSaveボタンを待たず即時適用・保存するため、結果値ではなくコールバックで呼び出し元へ渡す
    private readonly Action<AppThemeSetting> _applyTheme;
    private readonly Action<AppLanguageSetting> _applyLanguage;
    private int _fullScanIntervalHours;

    public IReadOnlyList<string> ResultRootPaths => _rootPaths.ToList();
    public IReadOnlyList<string> ResultExcludedPaths => _excludedPaths.ToList();
    public int ResultFullScanIntervalHours => _fullScanIntervalHours;
    public bool ShouldRunFullScan { get; private set; }

    /// <summary>チェックされた表示列のキー一覧（Name列は常時表示のため含まない）。</summary>
    public IReadOnlyList<string> ResultVisibleColumns =>
        _columnOptions
            .Where(option => option.CanToggleVisibility && option.IsVisible)
            .Select(option => option.Key)
            .ToList();

    /// <summary>ファイル一覧の列幅を既定値へ戻すかどうか（列幅はこの画面に持っていないため、フラグで呼び出し元へ伝える）。</summary>
    public bool ShouldResetColumnWidths { get; private set; }

    /// <summary>列の並び順（Nameを含む全ての列キー）。</summary>
    public IReadOnlyList<string> ResultColumnOrder =>
        _columnOptions.Select(option => option.Key).ToList();

    /// <summary>チェックされたツリー最上位ノードのキー一覧（常に表示のFoldersは含まない）。</summary>
    public IReadOnlyList<string> ResultVisibleTreeNodes =>
        _treeNodeOptions
            .Where(option => option.CanToggleVisibility && option.IsVisible)
            .Select(option => option.Key)
            .ToList();

    /// <summary>ツリー最上位のノードの並び順（Foldersを含む全てのノードキー）。</summary>
    public IReadOnlyList<string> ResultTreeNodeOrder =>
        _treeNodeOptions.Select(option => option.Key).ToList();

    // 設定画面に出す列名の対訳表キー。ファイル一覧のヘッダーだけでは分かりにくい列は補足付きの専用キーを使う
    private static readonly Dictionary<string, string> ColumnDisplayNameKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        [FileListColumns.Name] = "Settings.Column.Name",
        [FileListColumns.Location] = "Settings.Column.Location",
        [FileListColumns.Type] = "Column.Type",
        [FileListColumns.Size] = "Column.Size",
        [FileListColumns.Modified] = "Column.Modified",
        [FileListColumns.Created] = "Column.Created",
        [FileListColumns.Attributes] = "Settings.Column.Attributes"
    };

    // 設定画面に出すツリー最上位ノードの名前。ツリー上の表示名（絵文字つき）をそのまま使い、
    // 非表示にできないFoldersだけ補足を付ける
    private static string GetTreeNodeDisplayNameKey(string nodeKey)
    {
        return string.Equals(nodeKey, TreeNodes.AllRoots, StringComparison.OrdinalIgnoreCase)
            ? "Settings.TreeNode.AllRoots"
            : TreeNodes.GetDisplayNameKey(nodeKey);
    }

    // 現在の設定値でダイアログの初期状態を構築する
    public SettingsWindow(
        IEnumerable<string> currentRootPaths,
        IEnumerable<string> currentExcludedPaths,
        int currentFullScanIntervalHours,
        IEnumerable<string> currentVisibleColumns,
        IEnumerable<string> currentColumnOrder,
        IEnumerable<string> currentVisibleTreeNodes,
        IEnumerable<string> currentTreeNodeOrder,
        AppThemeSetting currentTheme,
        Action<AppThemeSetting> applyTheme,
        AppLanguageSetting currentLanguage,
        Action<AppLanguageSetting> applyLanguage,
        StoreLicenseService storeLicenseService,
        bool startOnSubscriptionPage = false)
    {
        InitializeComponent();

        _applyTheme = applyTheme;
        _applyLanguage = applyLanguage;
        _storeLicenseService = storeLicenseService;
        ApplyPlusLicenseState();

        // Plus機能のアンロック案内から開かれた場合は、最初からSubscriptionページを表示する
        if (startOnSubscriptionPage)
        {
            SettingsMenuListBox.SelectedItem = SubscriptionMenuItem;
        }

        // 現在のテーマのラジオを立てる。ここでCheckedハンドラが走るが、同値のため呼び出し先で無視される
        var themeRadioButton = currentTheme switch
        {
            AppThemeSetting.Light => LightThemeRadioButton,
            AppThemeSetting.Dark => DarkThemeRadioButton,
            _ => SystemThemeRadioButton
        };
        themeRadioButton.IsChecked = true;

        // 現在の言語のラジオを立てる。テーマと同じくここでCheckedハンドラが走るが、同値のため呼び出し先で無視される
        var languageRadioButton = currentLanguage switch
        {
            AppLanguageSetting.English => EnglishLanguageRadioButton,
            AppLanguageSetting.Japanese => JapaneseLanguageRadioButton,
            _ => SystemLanguageRadioButton
        };
        languageRadioButton.IsChecked = true;

        // 言語を切り替えると、この画面で組み立て済みの文字列（列名・購入ボタンの金額）も貼り替える
        AppLanguage.Changed += AppLanguage_Changed;
        Closed += (_, _) => AppLanguage.Changed -= AppLanguage_Changed;

        _rootPaths = new ObservableCollection<string>(currentRootPaths);
        _excludedPaths = new ObservableCollection<string>(currentExcludedPaths);
        _fullScanIntervalHours = NormalizeFullScanIntervalHours(currentFullScanIntervalHours);
        RootPathsListBox.ItemsSource = _rootPaths;
        ExcludedPathsListBox.ItemsSource = _excludedPaths;
        FullScanIntervalHoursTextBox.Text = _fullScanIntervalHours.ToString();

        _columnOptions = new ObservableCollection<ColumnOptionViewModel>(
            BuildColumnOptions(currentColumnOrder, currentVisibleColumns));

        ColumnOrderListBox.ItemsSource = _columnOptions;

        _treeNodeOptions = new ObservableCollection<ColumnOptionViewModel>(
            BuildTreeNodeOptions(currentTreeNodeOrder, currentVisibleTreeNodes));

        TreeNodeOrderListBox.ItemsSource = _treeNodeOptions;
    }

    // 言語切り替え時、XAMLのバインディングでは追従しない箇所を貼り替える
    private void AppLanguage_Changed(object? sender, EventArgs e)
    {
        foreach (var option in _columnOptions.Concat(_treeNodeOptions))
        {
            option.RefreshDisplayName();
        }

        ApplySubscribeButtonText();
    }

    // 指定された並び順・表示列から一覧の項目を組み立てる
    private static IEnumerable<ColumnOptionViewModel> BuildColumnOptions(
        IEnumerable<string> columnOrder,
        IEnumerable<string> visibleColumns)
    {
        var visibleColumnSet = visibleColumns.ToHashSet(StringComparer.OrdinalIgnoreCase);

        return OrderColumns(columnOrder).Select(column => new ColumnOptionViewModel(
            column,
            ColumnDisplayNameKeys[column],
            // Name列は常に表示。チェックを外せないよう、チェック済み・操作不可で出す
            isVisible: column == FileListColumns.Name || visibleColumnSet.Contains(column),
            canToggleVisibility: column != FileListColumns.Name));
    }

    // 指定された並び順・表示ノードから一覧の項目を組み立てる
    private static IEnumerable<ColumnOptionViewModel> BuildTreeNodeOptions(
        IEnumerable<string> treeNodeOrder,
        IEnumerable<string> visibleTreeNodes)
    {
        var visibleNodeSet = visibleTreeNodes.ToHashSet(StringComparer.OrdinalIgnoreCase);

        return OrderTreeNodes(treeNodeOrder).Select(node => new ColumnOptionViewModel(
            node,
            GetTreeNodeDisplayNameKey(node),
            // Foldersは常に表示（全て隠すとツリーが空になるため）。チェック済み・操作不可で出す
            isVisible: string.Equals(node, TreeNodes.AllRoots, StringComparison.OrdinalIgnoreCase) || visibleNodeSet.Contains(node),
            canToggleVisibility: !string.Equals(node, TreeNodes.AllRoots, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>指定された並び順に沿って全てのツリーノードを並べる（未知のキー・重複を除き、欠けたノードは既定の順で末尾に補う）。</summary>
    private static IEnumerable<string> OrderTreeNodes(IEnumerable<string> treeNodeOrder)
    {
        return OrderKeys(treeNodeOrder, TreeNodes.AllNodes);
    }

    /// <summary>指定された並び順に沿って全ての列を並べる（未知のキー・重複を除き、欠けた列は既定の順で末尾に補う）。</summary>
    private static IEnumerable<string> OrderColumns(IEnumerable<string> columnOrder)
    {
        return OrderKeys(columnOrder, FileListColumns.AllColumns);
    }

    // 指定された並び順を既知のキーだけに整える。未知のキー・重複は落とし、欠けたキーは既定の順で末尾に補う
    private static IEnumerable<string> OrderKeys(IEnumerable<string> order, IReadOnlyList<string> allKeys)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var key in order)
        {
            var known = allKeys.FirstOrDefault(x => string.Equals(x, key, StringComparison.OrdinalIgnoreCase));
            if (known is not null && seen.Add(known))
            {
                yield return known;
            }
        }

        foreach (var key in allKeys.Where(key => !seen.Contains(key)))
        {
            yield return key;
        }
    }

    // 選択中の列を1つ上へ移動する
    private void MoveColumnUpButton_Click(object sender, RoutedEventArgs e)
    {
        MoveSelectedColumn(-1);
    }

    // 選択中の列を1つ下へ移動する
    private void MoveColumnDownButton_Click(object sender, RoutedEventArgs e)
    {
        MoveSelectedColumn(1);
    }

    private void MoveSelectedColumn(int offset)
    {
        MoveSelectedItem(ColumnOrderListBox, _columnOptions, offset);
    }

    // 選択中のツリーノードを1つ上へ移動する
    private void MoveTreeNodeUpButton_Click(object sender, RoutedEventArgs e)
    {
        MoveSelectedItem(TreeNodeOrderListBox, _treeNodeOptions, -1);
    }

    // 選択中のツリーノードを1つ下へ移動する
    private void MoveTreeNodeDownButton_Click(object sender, RoutedEventArgs e)
    {
        MoveSelectedItem(TreeNodeOrderListBox, _treeNodeOptions, 1);
    }

    private static void MoveSelectedItem(ListBox listBox, ObservableCollection<ColumnOptionViewModel> options, int offset)
    {
        var index = listBox.SelectedIndex;
        var newIndex = index + offset;
        if (index < 0 || newIndex < 0 || newIndex >= options.Count)
        {
            return;
        }

        options.Move(index, newIndex);
        listBox.SelectedIndex = newIndex;
        listBox.ScrollIntoView(listBox.SelectedItem);
    }

    // 表示列・並び順・列幅をまとめて既定に戻す。表示列と並び順はその場で一覧へ反映し、
    // 列幅は呼び出し元が持っているためフラグで伝える。どちらもSaveで確定する（Cancelで閉じれば取り消せる）
    private void ResetColumnsButton_Click(object sender, RoutedEventArgs e)
    {
        ShouldResetColumnWidths = true;

        _columnOptions.Clear();
        foreach (var option in BuildColumnOptions(FileListColumns.AllColumns, FileListColumns.DefaultVisibleColumns))
        {
            _columnOptions.Add(option);
        }

        ResetColumnsHintTextBlock.Visibility = Visibility.Visible;
    }

    // 表示するツリーノードと並び順を既定に戻す。Saveで確定する（Cancelで閉じれば取り消せる）
    private void ResetTreeNodesButton_Click(object sender, RoutedEventArgs e)
    {
        _treeNodeOptions.Clear();
        foreach (var option in BuildTreeNodeOptions(TreeNodes.AllNodes, TreeNodes.DefaultVisibleNodes))
        {
            _treeNodeOptions.Add(option);
        }

        ResetTreeNodesHintTextBlock.Visibility = Visibility.Visible;
    }

    // Plusの購読状態をDisplay Columns/Subscriptionページへ反映する。
    // 未購読時はチェックボックス群を無効化（WPF標準の無効化スタイルで薄字・操作不可になる）し、アンロック案内を表示する
    private void ApplyPlusLicenseState()
    {
        var isActive = _storeLicenseService.IsPlusActive;
        ColumnCheckBoxesPanel.IsEnabled = isActive;
        PlusUpsellCard.Visibility = isActive ? Visibility.Collapsed : Visibility.Visible;
        TreeNodeCheckBoxesPanel.IsEnabled = isActive;
        TreeNodesPlusUpsellCard.Visibility = isActive ? Visibility.Collapsed : Visibility.Visible;

        // Subscriptionページ: 購読済みなら状態表示のみ、未購読なら購入ボタンを表示する
        PlusActiveTextBlock.Visibility = isActive ? Visibility.Visible : Visibility.Collapsed;
        SubscribePlusButton.Visibility = isActive ? Visibility.Collapsed : Visibility.Visible;

        if (!isActive)
        {
            _ = LoadPlusPriceAsync();
        }
    }

    // ストアから取得した表示価格（通貨ローカライズ済み）。取得できていなければnull
    private string? _plusFormattedPrice;

    // ストアから実際の表示価格を取得してボタンに反映する。取得できなければ金額なしの表記のまま
    private async Task LoadPlusPriceAsync()
    {
        _plusFormattedPrice = await _storeLicenseService.GetPlusFormattedPriceAsync();
        ApplySubscribeButtonText();
    }

    // 購入ボタンの文言を現在の言語・取得済みの価格で組み立てる
    private void ApplySubscribeButtonText()
    {
        SubscribePlusButton.Content = string.IsNullOrEmpty(_plusFormattedPrice)
            ? UiText.Get("Settings.Subscription.Subscribe")
            : UiText.Format("Settings.Subscription.SubscribeWithPrice", _plusFormattedPrice);
    }

    // 購入ダイアログを表示し、購読が成立したらチェックボックス群を有効化する
    private async void SubscribePlusButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_storeLicenseService.IsStoreAvailable)
        {
            PlusStatusTextBlock.Text = UiText.Get("Settings.Subscription.StoreUnavailable");
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
                PlusStatusTextBlock.Text = UiText.Get("Settings.Subscription.PurchaseNotCompleted");
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
        if (RootSettingsPanel is null || ColumnSettingsPanel is null || TreeNodeSettingsPanel is null || ThemePanel is null
            || LanguagePanel is null || SubscriptionPanel is null || SupportPanel is null)
        {
            return;
        }

        var selectedMenuItem = SettingsMenuListBox.SelectedItem;
        var showColumns = ReferenceEquals(selectedMenuItem, ColumnsMenuItem);
        var showTreeNodes = ReferenceEquals(selectedMenuItem, TreeNodesMenuItem);
        var showTheme = ReferenceEquals(selectedMenuItem, ThemeMenuItem);
        var showLanguage = ReferenceEquals(selectedMenuItem, LanguageMenuItem);
        var showSubscription = ReferenceEquals(selectedMenuItem, SubscriptionMenuItem);
        var showSupport = ReferenceEquals(selectedMenuItem, SupportMenuItem);
        var showRoot = !showColumns && !showTreeNodes && !showTheme && !showLanguage && !showSubscription && !showSupport;
        RootSettingsPanel.Visibility = showRoot ? Visibility.Visible : Visibility.Collapsed;
        ColumnSettingsPanel.Visibility = showColumns ? Visibility.Visible : Visibility.Collapsed;
        TreeNodeSettingsPanel.Visibility = showTreeNodes ? Visibility.Visible : Visibility.Collapsed;
        ThemePanel.Visibility = showTheme ? Visibility.Visible : Visibility.Collapsed;
        LanguagePanel.Visibility = showLanguage ? Visibility.Visible : Visibility.Collapsed;
        SubscriptionPanel.Visibility = showSubscription ? Visibility.Visible : Visibility.Collapsed;
        SupportPanel.Visibility = showSupport ? Visibility.Visible : Visibility.Collapsed;
        SaveAndFullScanButton.Visibility = showRoot ? Visibility.Visible : Visibility.Collapsed;
        // Theme/Language/Subscription/SupportページはSaveボタン経由で保存する設定を持たないため、Save/Cancelボタンも非表示にする
        var hasSaveTarget = showRoot || showColumns || showTreeNodes;
        SaveButton.Visibility = hasSaveTarget ? Visibility.Visible : Visibility.Collapsed;
        CancelButton.Visibility = hasSaveTarget ? Visibility.Visible : Visibility.Collapsed;
    }

    // テーマの切り替え。プレビューを兼ねるため、Saveボタンを待たずに即座に適用・保存する
    private void ThemeRadioButton_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton radioButton || radioButton.Tag is not string themeName)
        {
            return;
        }

        _applyTheme(AppTheme.Parse(themeName));
    }

    // 表示言語の切り替え。テーマと同じく、Saveボタンを待たずに即座に適用・保存する
    private void LanguageRadioButton_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton radioButton || radioButton.Tag is not string languageName)
        {
            return;
        }

        _applyLanguage(AppLanguage.Parse(languageName));
    }

    // Display ColumnsページのアンロックからSubscriptionページへ遷移する
    private void GoToSubscriptionButton_Click(object sender, RoutedEventArgs e)
    {
        SettingsMenuListBox.SelectedItem = SubscriptionMenuItem;
    }

    // Microsoftアカウントのサブスクリプション管理（解約）ページをブラウザで開く
    private void ManageSubscriptionButton_Click(object sender, RoutedEventArgs e)
    {
        OpenSupportUrl(_manageSubscriptionUrl, UiText.Get("Settings.Subscription.ManageCaption"));
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
            MessageBox.Show(UiText.Get("Settings.InvalidPath"), UiText.Get("Settings.InputErrorCaption"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // 応答の遅いNASパスでも設定画面を固めない（タイムアウト時は存在する扱いで受け付ける）
        if (!DirectoryAvailabilityChecker.ExistsOrTimedOut(normalized))
        {
            MessageBox.Show(UiText.Get("Settings.FolderNotFound"), UiText.Get("Settings.InputErrorCaption"), MessageBoxButton.OK, MessageBoxImage.Warning);
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
            MessageBox.Show(UiText.Get("Settings.InvalidPath"), UiText.Get("Settings.InputErrorCaption"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // 応答の遅いNASパスでも設定画面を固めない（タイムアウト時は存在する扱いで受け付ける）
        if (!DirectoryAvailabilityChecker.ExistsOrTimedOut(normalized))
        {
            MessageBox.Show(UiText.Get("Settings.FolderNotFound"), UiText.Get("Settings.InputErrorCaption"), MessageBoxButton.OK, MessageBoxImage.Warning);
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
            MessageBox.Show(UiText.Get("Settings.NoRootFolder"), UiText.Get("Settings.SaveErrorCaption"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (!int.TryParse(FullScanIntervalHoursTextBox.Text?.Trim(), out var parsedHours) || parsedHours <= 0)
        {
            MessageBox.Show(UiText.Get("Settings.InvalidInterval"), UiText.Get("Settings.SaveErrorCaption"), MessageBoxButton.OK, MessageBoxImage.Warning);
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
