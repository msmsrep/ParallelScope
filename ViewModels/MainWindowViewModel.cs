using System.Collections.ObjectModel;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using ParallelScope.Data;
using ParallelScope.Utilities;

namespace ParallelScope.ViewModels;

/// <summary>
/// メインウィンドウのViewModel。フォルダツリー・ファイル一覧・検索・スキャンの状態を保持する。
/// 各責務（設定/ナビゲーション/検索/スキャン/キャッシュ/表示アイテム管理）は partial クラスとしてファイル分割されている。
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    private ObservableCollection<FolderItemViewModel> _rootFolders;
    private ObservableCollection<FileItemViewModel> _fileItems;
    private string _currentPath = string.Empty;
    private string _addressInput = string.Empty;
    private string _searchQuery = string.Empty;
    private bool _isFlatFileViewEnabled;
    private readonly Stack<string> _backHistory = new();
    private readonly Stack<string> _forwardHistory = new();
    private readonly FileCacheRepository _fileCacheRepository;
    private readonly AppSettingsRepository _appSettingsRepository;
    private readonly SynchronizationContext _uiContext;
    private int _navigationVersion;
    private int _searchVersion;
    private int _flatViewVersion;
    private int _fullScanIntervalHours = AppSettings.DefaultFullScanIntervalHours;
    private HashSet<string> _excludedPaths = new(StringComparer.OrdinalIgnoreCase);
    private List<FileItemViewModel> _currentDirectoryItems = new();

    // バックグラウンド更新・検索・フォルダサイズ適用・フラット表示について、連続リクエストを1本化するキュー
    private readonly SingleFlightCoalescer<(string FolderPath, int NavigationVersion)> _refreshCoalescer;
    private readonly SingleFlightCoalescer<(string RootPath, string Query, int SearchVersion)> _searchCoalescer;
    private readonly SingleFlightCoalescer<(string FolderPath, IReadOnlyCollection<CachedFileSystemEntry> Entries, int NavigationVersion)> _folderSizeCoalescer;
    private readonly SingleFlightCoalescer<(string FolderPath, int FlatViewVersion)> _flatViewCoalescer;

    public ObservableCollection<FolderItemViewModel> RootFolders
    {
        get => _rootFolders;
        set => SetProperty(ref _rootFolders, value);
    }

    public ObservableCollection<FileItemViewModel> FileItems
    {
        get => _fileItems;
        set => SetProperty(ref _fileItems, value);
    }

    public string CurrentPath
    {
        get => _currentPath;
        set
        {
            if (SetProperty(ref _currentPath, value))
            {
                OnPropertyChanged(nameof(CanGoUp));
            }
        }
    }

    public string AddressInput
    {
        get => _addressInput;
        set => SetProperty(ref _addressInput, value);
    }

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (!SetProperty(ref _searchQuery, value))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                ClearSearch();
                return;
            }

            // 入力の都度、検索をリクエストする（インクリメンタルサーチ）
            RequestSearch(value);
        }
    }

    /// <summary>trueの場合、現在フォルダ直下ではなく配下の全ファイルを再帰的に表示する。</summary>
    public bool IsFlatFileViewEnabled
    {
        get => _isFlatFileViewEnabled;
        set
        {
            if (!SetProperty(ref _isFlatFileViewEnabled, value))
            {
                return;
            }

            SaveSettings(RootFolders.Select(x => x.Path));

            if (!string.IsNullOrWhiteSpace(SearchQuery))
            {
                // 検索結果表示中は表示を変えない（検索語をクリアした時点でモードに応じた一覧に切り替わる）
                return;
            }

            if (value)
            {
                RequestFlatFileView();
            }
            else
            {
                ReplaceVisibleFileItems(_currentDirectoryItems);
            }
        }
    }

    public bool CanGoBack => _backHistory.Count > 0;

    public bool CanGoForward => _forwardHistory.Count > 0;

    public bool CanGoUp => GetParentPath(CurrentPath) is not null;

    public MainWindowViewModel()
    {
        _rootFolders = new ObservableCollection<FolderItemViewModel>();
        _fileItems = new ObservableCollection<FileItemViewModel>();
        _fileCacheRepository = new FileCacheRepository();
        _appSettingsRepository = new AppSettingsRepository();
        _uiContext = SynchronizationContext.Current ?? new SynchronizationContext();

        _refreshCoalescer = new SingleFlightCoalescer<(string FolderPath, int NavigationVersion)>(
            request => RefreshFromFileSystemInBackground(request.FolderPath, request.NavigationVersion));
        _searchCoalescer = new SingleFlightCoalescer<(string RootPath, string Query, int SearchVersion)>(
            request => SearchInBackground(request.RootPath, request.Query, request.SearchVersion));
        _folderSizeCoalescer = new SingleFlightCoalescer<(string FolderPath, IReadOnlyCollection<CachedFileSystemEntry> Entries, int NavigationVersion)>(
            request => ApplyCachedFolderSizesInBackground(request.FolderPath, request.Entries, request.NavigationVersion));
        _flatViewCoalescer = new SingleFlightCoalescer<(string FolderPath, int FlatViewVersion)>(
            request => ApplyFlatFileView(request.FolderPath, request.FlatViewVersion));

        InitializeRootFolders();
    }
}
