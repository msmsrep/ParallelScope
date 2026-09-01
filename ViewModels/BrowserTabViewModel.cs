using System.Collections.ObjectModel;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using ParallelScope.Data;
using ParallelScope.Utilities;

namespace ParallelScope.ViewModels;

/// <summary>
/// 1つの閲覧状態（タブ）を表すViewModel。現在パス・戻る/進む履歴・検索語・All Filesモードと、
/// それらに対応するファイル一覧を保持する。アプリ全体で1つしかない状態（設定・キャッシュDB・
/// お気に入り等）は持たず、<see cref="IBrowserTabHost"/> 経由でシェルから借りる。
/// 各責務（ナビゲーション/検索/フラット表示/キャッシュ/表示アイテム管理）は partial クラスとしてファイル分割されている。
/// </summary>
public partial class BrowserTabViewModel : ObservableObject
{
    private readonly IBrowserTabHost _host;

    private ObservableCollection<FileItemViewModel> _fileItems = new();
    private string _currentPath = string.Empty;
    private string _addressInput = string.Empty;
    private string _searchQuery = string.Empty;
    private bool _isFlatFileViewEnabled;
    private readonly Stack<string> _backHistory = new();
    private readonly Stack<string> _forwardHistory = new();
    private int _navigationVersion;
    private int _searchVersion;
    private int _flatViewVersion;
    private List<FileItemViewModel> _currentDirectoryItems = new();

    // バックグラウンド更新・検索・フォルダサイズ適用・フラット表示について、連続リクエストを1本化するキュー
    private readonly SingleFlightCoalescer<(string FolderPath, int NavigationVersion)> _refreshCoalescer;
    private readonly SingleFlightCoalescer<(string RootPath, string Query, int SearchVersion, bool FilesOnly)> _searchCoalescer;
    private readonly SingleFlightCoalescer<(string FolderPath, IReadOnlyCollection<CachedFileSystemEntry> Entries, int NavigationVersion)> _folderSizeCoalescer;
    private readonly SingleFlightCoalescer<(string FolderPath, int FlatViewVersion)> _flatViewCoalescer;

    internal BrowserTabViewModel(IBrowserTabHost host)
    {
        _host = host;

        _refreshCoalescer = new SingleFlightCoalescer<(string FolderPath, int NavigationVersion)>(
            request => RefreshFromFileSystemInBackground(request.FolderPath, request.NavigationVersion));
        _searchCoalescer = new SingleFlightCoalescer<(string RootPath, string Query, int SearchVersion, bool FilesOnly)>(
            request => SearchInBackground(request.RootPath, request.Query, request.SearchVersion, request.FilesOnly));
        _folderSizeCoalescer = new SingleFlightCoalescer<(string FolderPath, IReadOnlyCollection<CachedFileSystemEntry> Entries, int NavigationVersion)>(
            request => ApplyCachedFolderSizesInBackground(request.FolderPath, request.Entries, request.NavigationVersion));
        _flatViewCoalescer = new SingleFlightCoalescer<(string FolderPath, int FlatViewVersion)>(
            request => ApplyFlatFileView(request.FolderPath, request.FlatViewVersion));
    }

    /// <summary>画面に表示中のファイル一覧（検索結果・フラット表示・通常一覧のいずれか）。</summary>
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

            _host.OnFlatFileViewEnabledChanged();

            if (!string.IsNullOrWhiteSpace(SearchQuery))
            {
                // 検索結果はモードによってフォルダの表示有無が変わるため、同じ検索語で再検索して反映する
                RequestSearch(SearchQuery);
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

    /// <summary>
    /// 保存済み設定の読み込み時に、All Filesモードの初期値を副作用なしで設定する。
    /// プロパティセッター経由だとCurrentPath未設定の状態で取得リクエストが走ってしまうため、
    /// 初期化時だけはこちらを使う。
    /// </summary>
    internal void InitializeFlatFileViewEnabled(bool isEnabled)
    {
        if (_isFlatFileViewEnabled == isEnabled)
        {
            return;
        }

        _isFlatFileViewEnabled = isEnabled;
        OnPropertyChanged(nameof(IsFlatFileViewEnabled));
    }

    /// <summary>表示できるルートが1つも無くなった場合に、現在地と一覧を空にする。</summary>
    internal void Clear()
    {
        CurrentPath = string.Empty;
        AddressInput = string.Empty;
        _currentDirectoryItems.Clear();
        ReplaceVisibleFileItems(Array.Empty<FileItemViewModel>());
    }

    public bool CanGoBack => _backHistory.Count > 0;

    public bool CanGoForward => _forwardHistory.Count > 0;

    public bool CanGoUp => GetParentPath(CurrentPath) is not null;

    /// <summary>
    /// 対象パス同士が入れ子（例: D:\ と D:\Sub）になっているかどうか。
    /// 横断列挙（All Files・横断検索）で同一エントリの重複除去が必要かの判定に使う。
    /// </summary>
    private static bool HasOverlappingPaths(IReadOnlyList<string> paths)
    {
        for (var i = 0; i < paths.Count; i++)
        {
            for (var j = 0; j < paths.Count; j++)
            {
                if (i != j && PathNormalizer.IsAncestorOrSame(paths[i], paths[j]))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
