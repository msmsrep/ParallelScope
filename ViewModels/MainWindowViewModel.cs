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
    private ObservableCollection<FolderItemViewModel> _rootFolders;
    private readonly BrowserTabViewModel _activeTab;
    private readonly FileCacheRepository _fileCacheRepository;
    private readonly AppSettingsRepository _appSettingsRepository;
    private readonly SynchronizationContext _uiContext;
    private int _fullScanIntervalHours = AppSettings.DefaultFullScanIntervalHours;
    private HashSet<string> _excludedPaths = new(StringComparer.OrdinalIgnoreCase);
    // 開発者専用のPlus解放キー。設定画面では編集できないため、SaveSettingsで消えないよう読み込んだ値を保持し続ける
    private string? _developerUnlockKey;
    private AppThemeSetting _theme = AppThemeSetting.System;
    private AppLanguageSetting _language = AppLanguageSetting.System;

    // バックグラウンド処理（横断検索・フラット表示・Roots一覧）から参照するルートパスの不変スナップショット。
    // RootFolders（ObservableCollection）はUIスレッド専用のため、コアレサーのハンドラから直接触らない
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

    public ObservableCollection<FolderItemViewModel> RootFolders
    {
        get => _rootFolders;
        set => SetProperty(ref _rootFolders, value);
    }

    /// <summary>
    /// フォルダツリーに表示する最上位ノード。全ルートを子に持つ仮想「Folders」ノード1件のみを含み、
    /// ルートの増減は共有している RootFolders コレクション経由で自動的に反映される。
    /// </summary>
    public ObservableCollection<FolderItemViewModel> TreeRoots { get; } = new();

    /// <summary>
    /// 現在表示中のタブ。複数タブ対応（フェーズ3）まではアプリ全体で1つだけ存在する。
    /// </summary>
    public BrowserTabViewModel ActiveTab => _activeTab;

    // ここから下は、アクティブなタブの状態をそのまま見せるための委譲。
    // 画面のバインディングとコードビハインドは引き続きこのViewModelだけを見ればよい状態を保つ
    public ObservableCollection<FileItemViewModel> FileItems => _activeTab.FileItems;

    public string CurrentPath => _activeTab.CurrentPath;

    public string AddressInput
    {
        get => _activeTab.AddressInput;
        set => _activeTab.AddressInput = value;
    }

    public string SearchQuery
    {
        get => _activeTab.SearchQuery;
        set => _activeTab.SearchQuery = value;
    }

    /// <summary>trueの場合、現在フォルダ直下ではなく配下の全ファイルを再帰的に表示する。</summary>
    public bool IsFlatFileViewEnabled
    {
        get => _activeTab.IsFlatFileViewEnabled;
        set => _activeTab.IsFlatFileViewEnabled = value;
    }

    public bool CanGoBack => _activeTab.CanGoBack;

    public bool CanGoForward => _activeTab.CanGoForward;

    public bool CanGoUp => _activeTab.CanGoUp;

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
        _rootFolders = new ObservableCollection<FolderItemViewModel>();
        _fileCacheRepository = fileCacheRepository;
        _appSettingsRepository = appSettingsRepository;
        _uiContext = SynchronizationContext.Current ?? new SynchronizationContext();

        // ツリーの初期化・設定の読み込みはどちらも現在パス（＝タブの状態）を参照するため、タブを先に用意する
        _activeTab = new BrowserTabViewModel(this);
        _activeTab.PropertyChanged += ActiveTab_PropertyChanged;

        InitializeTreeNodes();
        InitializeRootFolders();
    }

    // タブ側の状態変化を、そのまま同名プロパティの変更通知として画面へ流す
    private void ActiveTab_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(e.PropertyName);
    }
}
