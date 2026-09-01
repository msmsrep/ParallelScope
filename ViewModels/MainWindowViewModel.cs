using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using ParallelScope.Data;
using ParallelScope.Utilities;

namespace ParallelScope.ViewModels;

/// <summary>
/// メインウィンドウのViewModel（シェル）。アプリ全体で1つしかない状態 —— 設定・キャッシュDB・
/// フォルダツリー・お気に入り／アクセス実績・スキャン —— を持つ。
/// 現在パス・履歴・検索・ファイル一覧といった「1つの閲覧状態」は <see cref="BrowserTabViewModel"/> 側にあり、
/// 画面（XAML・コードビハインド）から見えるプロパティはアクティブなタブへの委譲になっている。
/// 各責務（設定/スキャン/お気に入り/タブの窓口）は partial クラスとしてファイル分割されている。
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    // 変更通知を画面へ中継しているタブ（アクティブなタブが切り替わるたびに繋ぎ替える）
    private BrowserTabViewModel _observedTab;
    private readonly FileCacheRepository _fileCacheRepository;
    private readonly AppSettingsRepository _appSettingsRepository;
    private readonly SynchronizationContext _uiContext;
    private int _fullScanIntervalHours = AppSettings.DefaultFullScanIntervalHours;
    private HashSet<string> _excludedPaths = new(StringComparer.OrdinalIgnoreCase);
    // 開発者専用のPlus解放キー。設定画面では編集できないため、SaveSettingsで消えないよう読み込んだ値を保持し続ける
    private string? _developerUnlockKey;
    private AppThemeSetting _theme = AppThemeSetting.System;
    private AppLanguageSetting _language = AppLanguageSetting.System;

    // 設定されているルートパス（正規化済み。除外設定に該当するものも含む＝settings.jsonに保存する内容）
    private List<string> _rootPaths = new();

    // 実際にツリー・横断列挙の対象にするルートパス（除外設定に該当するものを除いた不変スナップショット）。
    // ツリーのノード（ObservableCollection）はUIスレッド専用のため、コアレサーのハンドラから直接触らない
    private IReadOnlyList<string> _rootPathsSnapshot = Array.Empty<string>();

    /// <summary>
    /// 横断列挙（All Files・横断検索）の起点となるパス群を返す。
    /// 仮想ノード（Folders / Favorites / Frequently Used）なら対応するフォルダ群、実パスならそのパス自身。
    /// </summary>
    private IReadOnlyList<string> GetTraversalPaths(string path)
    {
        var kind = VirtualFolders.GetKind(path);
        return kind == VirtualFolderKind.None
            ? new[] { path }
            : GetVirtualFolderPaths(kind);
    }

    // ファイル一覧の列の並び順（列キー。Nameを含む）と、ユーザーが変更した列幅（列キー→ピクセル幅）。
    // どちらもPlus機能のため、反映するかどうかはコードビハインド側が購読状態で判断する
    private List<string> _columnOrder = FileListColumns.AllColumns.ToList();
    private Dictionary<string, double> _columnWidths = new(StringComparer.OrdinalIgnoreCase);

    // CSV出力でSize列を生のバイト数で書き出すか（保存ダイアログのファイル種類で選ばれた前回の値）
    private bool _csvExportSizeInBytes;

    // ファイル一覧に表示する列キー（FileListColumns参照。Name列は常時表示のため含まない）
    private HashSet<string> _visibleColumns = FileListColumns.DefaultVisibleColumns.ToHashSet(StringComparer.OrdinalIgnoreCase);

    // 隠し属性・システム属性のファイル/フォルダを出すか（既定は表示。この設定を入れる前と同じ見え方）
    private bool _showHiddenItems = true;
    private bool _showSystemItems = true;

    /// <summary>
    /// 閲覧ペイン。2画面表示（フェーズ4）で2件になるまでは1件だけ存在する。
    /// </summary>
    public ObservableCollection<BrowserPaneViewModel> Panes { get; } = new();

    /// <summary>操作対象のペイン（メニューやスキャンの反映先）。</summary>
    public BrowserPaneViewModel ActivePane => Panes[0];

    /// <summary>操作対象のペインで表示中のタブ。</summary>
    public BrowserTabViewModel ActiveTab => ActivePane.ActiveTab;

    // ツリーはペインごとの持ち物。画面・テストから見えるこれらは操作対象のペインのものを指す
    public ObservableCollection<FolderItemViewModel> RootFolders => ActivePane.RootFolders;

    public ObservableCollection<FolderItemViewModel> TreeRoots => ActivePane.TreeRoots;

    /// <summary>ツリー最上位の「Folders」ノード。</summary>
    public FolderItemViewModel AllRootsNode => ActivePane.AllRootsNode;

    /// <summary>開いているすべてのタブ（設定変更をタブ全体へ反映するために使う）。</summary>
    private IEnumerable<BrowserTabViewModel> AllTabs => Panes.SelectMany(pane => pane.Tabs);

    // ここから下は、アクティブなタブの状態をそのまま見せるための委譲。
    // 画面のバインディングとコードビハインドは引き続きこのViewModelだけを見ればよい状態を保つ
    public ObservableCollection<FileItemViewModel> FileItems => ActiveTab.FileItems;

    public string CurrentPath => ActiveTab.CurrentPath;

    public string AddressInput
    {
        get => ActiveTab.AddressInput;
        set => ActiveTab.AddressInput = value;
    }

    public string SearchQuery
    {
        get => ActiveTab.SearchQuery;
        set => ActiveTab.SearchQuery = value;
    }

    /// <summary>trueの場合、現在フォルダ直下ではなく配下の全ファイルを再帰的に表示する。</summary>
    public bool IsFlatFileViewEnabled
    {
        get => ActiveTab.IsFlatFileViewEnabled;
        set => ActiveTab.IsFlatFileViewEnabled = value;
    }

    public bool CanGoBack => ActiveTab.CanGoBack;

    public bool CanGoForward => ActiveTab.CanGoForward;

    public bool CanGoUp => ActiveTab.CanGoUp;

    public MainWindowViewModel()
        : this(new FileCacheRepository(), new AppSettingsRepository())
    {
    }

    /// <summary>
    /// 保存先を差し替えたリポジトリを渡して生成する（単体テスト用）。
    /// アプリ本体は引数なしのコンストラクタを使い、リポジトリはここで直接newする。
    /// </summary>
    internal MainWindowViewModel(FileCacheRepository fileCacheRepository, AppSettingsRepository appSettingsRepository)
    {
        _fileCacheRepository = fileCacheRepository;
        _appSettingsRepository = appSettingsRepository;
        _uiContext = SynchronizationContext.Current ?? new SynchronizationContext();

        // 設定の読み込みはペインのツリーとタブへ反映されるため、ペインを先に用意する
        var pane = new BrowserPaneViewModel(this);
        Panes.Add(pane);
        pane.PropertyChanged += ActivePane_PropertyChanged;
        _observedTab = pane.ActiveTab;
        _observedTab.PropertyChanged += ActiveTab_PropertyChanged;

        InitializeRootFolders();
    }

    // タブ側の状態変化を、そのまま同名プロパティの変更通知として画面へ流す
    private void ActiveTab_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(e.PropertyName);
    }

    // 表示中のタブが切り替わったら、中継先を繋ぎ替えて委譲プロパティをまとめて通知し直す
    private void ActivePane_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(BrowserPaneViewModel.ActiveTab))
        {
            return;
        }

        _observedTab.PropertyChanged -= ActiveTab_PropertyChanged;
        _observedTab = ActiveTab;
        _observedTab.PropertyChanged += ActiveTab_PropertyChanged;

        OnPropertyChanged(nameof(ActiveTab));
        OnPropertyChanged(nameof(FileItems));
        OnPropertyChanged(nameof(CurrentPath));
        OnPropertyChanged(nameof(AddressInput));
        OnPropertyChanged(nameof(SearchQuery));
        OnPropertyChanged(nameof(IsFlatFileViewEnabled));
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        OnPropertyChanged(nameof(CanGoUp));
    }

    /// <summary>全ペインのツリーで、ルートフォルダのスキャン中表示を一括で切り替える。</summary>
    public void SetRootScanningState(bool isScanning)
    {
        foreach (var rootFolder in Panes.SelectMany(pane => pane.RootFolders))
        {
            rootFolder.IsScanning = isScanning;
        }
    }

    /// <summary>
    /// スキャン完了後、表示していないタブに「キャッシュが更新された」印を付ける。
    /// 印の付いたタブは次に表示されるときに一覧を読み直す（全タブを同時に再取得しないため）。
    /// </summary>
    public void MarkInactiveTabsStale()
    {
        foreach (var tab in AllTabs.Where(tab => !tab.IsActive))
        {
            tab.MarkStale();
        }
    }

    /// <summary>言語切り替え後に、仮想ノードを開いているタブの見出しを引き直す。</summary>
    private void RefreshLocalizedTabNames()
    {
        foreach (var tab in AllTabs)
        {
            tab.RefreshLocalizedDisplayName();
        }
    }
}
