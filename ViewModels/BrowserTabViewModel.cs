using System.Collections.ObjectModel;
using System.IO;
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

    // キャッシュ由来の一覧を表示し終えたナビゲーション番号。ライブ更新がキャッシュと同内容だった場合に
    // 一覧の作り直しを省けるかどうかの判定に使う（BrowserTabViewModel.Cache.cs）
    private int _cacheAppliedNavigationVersion = -1;
    private List<FileItemViewModel> _currentDirectoryItems = new();
    private bool _isActive;
    private bool _isSuspended;
    private bool _isStale;

    // バックグラウンド更新・検索・フォルダサイズ適用・フラット表示について、連続リクエストを1本化するキュー
    private readonly SingleFlightCoalescer<(string FolderPath, int NavigationVersion)> _refreshCoalescer;
    private readonly SingleFlightCoalescer<(string RootPath, NameSearchPattern Pattern, int SearchVersion, bool FilesOnly)> _searchCoalescer;
    private readonly SingleFlightCoalescer<(string FolderPath, IReadOnlyCollection<CachedFileSystemEntry> Entries, int NavigationVersion)> _folderSizeCoalescer;
    private readonly SingleFlightCoalescer<(string FolderPath, int FlatViewVersion)> _flatViewCoalescer;

    internal BrowserTabViewModel(IBrowserTabHost host)
    {
        _host = host;

        _refreshCoalescer = new SingleFlightCoalescer<(string FolderPath, int NavigationVersion)>(
            request => RefreshFromFileSystemInBackground(request.FolderPath, request.NavigationVersion));
        _searchCoalescer = new SingleFlightCoalescer<(string RootPath, NameSearchPattern Pattern, int SearchVersion, bool FilesOnly)>(
            request => SearchInBackground(request.RootPath, request.Pattern, request.SearchVersion, request.FilesOnly));
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
                OnPropertyChanged(nameof(DisplayName));
                OnPropertyChanged(nameof(ToolTipText));
            }
        }
    }

    /// <summary>タブ見出しに出す名前（現在フォルダ名。仮想ノードは表示言語に応じた名前）。</summary>
    public string DisplayName => BuildDisplayName(CurrentPath);

    /// <summary>タブ見出しのツールチップ。実パスはフルパス、仮想ノードは表示名をそのまま出す。</summary>
    public string ToolTipText => VirtualFolders.IsVirtual(CurrentPath) ? DisplayName : CurrentPath;

    /// <summary>このタブが属するペインで表示中かどうか（タブ見出しの強調表示に使う）。</summary>
    public bool IsActive
    {
        get => _isActive;
        internal set => SetProperty(ref _isActive, value);
    }

    /// <summary>ドラッグ中のタブをこのタブの手前に落とす位置にいるか（挿入位置の目印の表示）。</summary>
    public bool IsDropTargetBefore
    {
        get => _isDropTargetBefore;
        internal set => SetProperty(ref _isDropTargetBefore, value);
    }

    /// <summary>ドラッグ中のタブをこのタブの後ろに落とす位置にいるか（挿入位置の目印の表示）。</summary>
    public bool IsDropTargetAfter
    {
        get => _isDropTargetAfter;
        internal set => SetProperty(ref _isDropTargetAfter, value);
    }

    private bool _isDropTargetBefore;
    private bool _isDropTargetAfter;

    /// <summary>
    /// 一覧のソート状態（列キーと昇順かどうか）。DataGrid側の状態はタブを切り替えると失われるため、
    /// タブごとにここへ控えて、表示し直すときにビューが復元する。nullは既定の並び（ソート指定なし）。
    /// </summary>
    internal string? SortColumnKey { get; set; }

    internal bool IsSortAscending { get; set; } = true;

    /// <summary>言語切り替え後に、仮想ノードを開いているタブの見出しを引き直す。</summary>
    internal void RefreshLocalizedDisplayName()
    {
        if (!VirtualFolders.IsVirtual(CurrentPath))
        {
            return;
        }

        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(ToolTipText));
    }

    private static string BuildDisplayName(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var kind = VirtualFolders.GetKind(path);
        if (kind != VirtualFolderKind.None)
        {
            return VirtualFolders.GetDisplayName(kind);
        }

        // ドライブ直下（"D:\"）やUNC共有ルートはフォルダ名が取れないため、パスをそのまま見出しにする
        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.IsNullOrWhiteSpace(name) ? path : name;
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

    /// <summary>
    /// 検索語が正規表現として成立していないか（正規表現モードのときだけ真になりうる）。
    /// 打ちかけの正規表現でも一覧は据え置くため、書きかけであることは入力欄の色で示す。
    /// </summary>
    public bool IsSearchQueryInvalid
    {
        get => _isSearchQueryInvalid;
        private set => SetProperty(ref _isSearchQueryInvalid, value);
    }

    private bool _isSearchQueryInvalid;

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

    /// <summary>
    /// 表示していない間、一覧の実体を手放してメモリを返す（フラット表示では数十万件になりうるため）。
    /// 小さい一覧はそのまま持っておく（切り替えのたびに空表示を挟まないため）。
    /// </summary>
    internal void SuspendIfHeavy()
    {
        if (FileItems.Count < SuspendItemCountThreshold && _currentDirectoryItems.Count < SuspendItemCountThreshold)
        {
            return;
        }

        _isSuspended = true;
        ForgetCompletedSearch();
        _currentDirectoryItems = new List<FileItemViewModel>();
        FileItems = new ObservableCollection<FileItemViewModel>();
    }

    /// <summary>
    /// 表示していない間にキャッシュが更新された（スキャンが走った）ことを記録する。
    /// 全タブをその場で読み直すと数十本の再取得が同時に走るため、次に表示するときまで遅らせる。
    /// </summary>
    internal void MarkStale()
    {
        _isStale = true;
        // キャッシュが変わった以上、控えてある検索結果からは絞り込めない
        ForgetCompletedSearch();
    }

    /// <summary>再び表示する際に、手放していた一覧・古くなった一覧をキャッシュから読み直す。</summary>
    internal void OnActivated()
    {
        if (!_isSuspended && !_isStale)
        {
            return;
        }

        _isSuspended = false;
        _isStale = false;
        RefreshCurrentFolder();
    }

    /// <summary>この件数以上の一覧を持つタブは、非表示になった時点で一覧を手放す。</summary>
    private const int SuspendItemCountThreshold = 5_000;

    /// <summary>閉じたタブを開き直すために、復元に必要な状態を控える。</summary>
    internal ClosedTabState CreateClosedState(int index)
    {
        return new ClosedTabState(
            CurrentPath,
            IsFlatFileViewEnabled,
            _backHistory.ToArray(),
            _forwardHistory.ToArray(),
            index);
    }

    /// <summary>閉じたタブの状態（パス・モード・履歴）を復元する。</summary>
    internal void RestoreClosedState(ClosedTabState state)
    {
        InitializeFlatFileViewEnabled(state.IsFlatFileViewEnabled);
        NavigateTo(state.Path, false);

        // Stack は先頭が新しい順で控えているため、逆順に積み直して元の並びに戻す
        _backHistory.Clear();
        foreach (var path in state.BackHistory.Reverse())
        {
            _backHistory.Push(path);
        }

        _forwardHistory.Clear();
        foreach (var path in state.ForwardHistory.Reverse())
        {
            _forwardHistory.Push(path);
        }

        NotifyNavigationStateChanged();
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
