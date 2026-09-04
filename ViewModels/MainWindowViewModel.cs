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
    // 除外判定用に、除外パスと「区切り文字付きの接頭辞」を作り置きした配列（SetExcludedPathsで更新）
    private (string Path, string Prefix)[] _excludedPathMatchers = Array.Empty<(string, string)>();
    // 開発者専用のPlus解放キー。設定画面では編集できないため、SaveSettingsで消えないよう読み込んだ値を保持し続ける
    private string? _developerUnlockKey;
    private AppThemeSetting _theme = AppThemeSetting.System;
    private AppLanguageSetting _language = AppLanguageSetting.System;

    // 起動時の組み立て中は設定を書き出さない（読み込んだ内容を途中の状態で上書きしないため）
    private bool _isInitialized;

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

    /// <summary>閲覧ペイン（1画面なら1件、2画面表示なら2件）。</summary>
    public ObservableCollection<BrowserPaneViewModel> Panes { get; } = new();

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
        var pane = CreatePane();
        pane.IsActive = true;
        Panes.Add(pane);
        _observedTab = pane.ActiveTab;
        _observedTab.PropertyChanged += ActiveTab_PropertyChanged;

        InitializeRootFolders();
        _isInitialized = true;
    }

    // タブ側の状態変化を、そのまま同名プロパティの変更通知として画面へ流す
    private void ActiveTab_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(e.PropertyName);
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
    /// スキャン完了後の一覧の作り直し。表示中のタブ（各ペインのアクティブタブ）はその場で読み直し、
    /// 表示していないタブには印だけ付けて次に表示するときに読み直す（全タブを同時に再取得しないため）。
    /// </summary>
    /// <param name="scannedPath">
    /// スキャンしたフォルダ。指定した場合、その配下を表示しているタブだけを読み直す（nullなら全ルート＝全て対象）。
    /// </param>
    public void RefreshAfterScan(string? scannedPath = null)
    {
        foreach (var tab in Panes.Select(pane => pane.ActiveTab))
        {
            if (string.IsNullOrWhiteSpace(tab.CurrentPath))
            {
                continue;
            }

            if (scannedPath is not null && !PathNormalizer.IsAncestorOrSame(scannedPath, tab.CurrentPath))
            {
                continue;
            }

            tab.RefreshCurrentFolder();
        }

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
