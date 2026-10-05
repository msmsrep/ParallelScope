using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using ParallelScope.Data;
using ParallelScope.Utilities;

namespace ParallelScope.ViewModels;

/// <summary>フォルダツリーの1ノードを表すViewModel。子フォルダは展開時に遅延読み込みされる。</summary>
public class FolderItemViewModel : ObservableObject
{
    private static EnumerationOptions _nonRecursiveEnumerationOptions =
        CreateEnumerationOptions(HiddenItemVisibility.AlwaysSkippedAttributes);

    /// <summary>
    /// 子フォルダの列挙で飛ばす属性（隠し・システムフォルダを出すかの設定を反映する）。
    /// 設定はアプリ全体で1つなので、ノードごとに持たせず静的に共有する。
    /// 差し替えても読み込み済みの子は入れ替わらないため、変更後は <see cref="Reload"/> を呼ぶこと。
    /// </summary>
    public static FileAttributes AttributesToSkip
    {
        get => _nonRecursiveEnumerationOptions.AttributesToSkip;
        set => _nonRecursiveEnumerationOptions = CreateEnumerationOptions(value);
    }

    private static EnumerationOptions CreateEnumerationOptions(FileAttributes attributesToSkip) => new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
        AttributesToSkip = attributesToSkip
    };

    // 遅延読み込み中のダミーノードの表示名（対訳表のキー）
    private const string LoadingDisplayNameKey = "Tree.Loading";

    private readonly string _path;
    private readonly Func<string, bool>? _isExcludedPath;
    private readonly Func<string, IReadOnlyList<CachedFileSystemEntry>>? _getCachedSubFolders;
    // 読み込みの世代。Reload で読み直しが始まったら、古い読み込みの結果は反映しない
    private int _loadVersion;

    /// <summary>キャッシュで先に表示したあと裏で続けている、ファイルシステムからの読み直し（単体テストで待つため）。</summary>
    internal Task BackgroundRefresh { get; private set; } = Task.CompletedTask;
    private readonly bool _isShortcut;
    private ObservableCollection<FolderItemViewModel>? _subFolders;
    private bool _isScanning;
    private bool _isLoaded;
    private bool _hasSubFolders = true;
    private bool _isExpanded;
    private ImageSource? _iconSource;
    private string _displayName = string.Empty;
    // 仮想ノード・遅延読み込み中のダミーは表示名が言語で変わるため、対訳表のキーを控えて言語切り替え時に引き直す
    private string? _displayNameKey;

    /// <summary>ツリーに表示する名前。言語切り替えで変わりうるため変更通知を出す。</summary>
    public string DisplayName
    {
        get => _displayName;
        set => SetProperty(ref _displayName, value);
    }

    public string Path => _path;

    /// <summary>
    /// お気に入り／よく使う配下のノード（実体ツリーの複製）かどうか。
    /// 同じパスのノードがツリー上に複数現れるため、パス→TreeViewItemのマップには実体ツリー側だけを登録する
    /// （複製を登録すると、実体ツリーで選択したつもりが複製側へ飛んでしまう）。
    /// </summary>
    public bool IsShortcut => _isShortcut;

    /// <summary>ツリーに表示するツールチップ。同名フォルダが並びうる複製ノードのみフルパスを出す。</summary>
    public string? ToolTipText => _isShortcut ? _path : null;

    public ImageSource? IconSource
    {
        get => _iconSource;
        set => SetProperty(ref _iconSource, value);
    }

    /// <summary>フォルダアイコンの代わりにツリーへ表示する記号（★ など）。持たないノードはnullでアイコンを出す。</summary>
    public string? GlyphIcon { get; }

    public bool IsScanning
    {
        get => _isScanning;
        set => SetProperty(ref _isScanning, value);
    }

    public bool HasSubFolders
    {
        get => _hasSubFolders;
        set => SetProperty(ref _hasSubFolders, value);
    }

    /// <summary>ツリー上の展開状態。仮想「Folders」ノードを既定で展開表示するために持つ。</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    /// <summary>
    /// 子フォルダ。読み込み時はコレクションのインスタンスごと差し替える（<see cref="SetSubFolders"/>）ため、
    /// 差し替えを画面へ伝えられるよう変更通知を出す。
    /// 仮想ノードだけは呼び出し側のコレクションを共有しており、差し替えは行わない。
    /// </summary>
    public ObservableCollection<FolderItemViewModel> SubFolders
    {
        get
        {
            _subFolders ??= new ObservableCollection<FolderItemViewModel>();
            return _subFolders;
        }
    }

    /// <summary>
    /// 子フォルダをまとめて入れ替える。
    /// 1件ずつ Add すると件数分の CollectionChanged がUIスレッドで発生し、
    /// 子が2万を超えるフォルダ（C:\Windows\WinSxS など）では展開だけで画面が固まるため、
    /// ファイル一覧（<c>ReplaceVisibleFileItems</c>）と同じくインスタンスごと差し替えて通知を1回にする。
    /// </summary>
    private void SetSubFolders(IEnumerable<FolderItemViewModel> subFolders)
    {
        _subFolders = new ObservableCollection<FolderItemViewModel>(subFolders);
        OnPropertyChanged(nameof(SubFolders));
    }

    /// <param name="getCachedSubFolders">
    /// キャッシュDBに載っている直下の子フォルダを返す（バックグラウンドから呼ばれる。省略時はファイルシステムだけを読む）。
    /// 子ノードにも引き継ぐ。
    /// </param>
    public FolderItemViewModel(
        string path,
        Func<string, bool>? isExcludedPath = null,
        bool isShortcut = false,
        Func<string, IReadOnlyList<CachedFileSystemEntry>>? getCachedSubFolders = null)
    {
        _path = path;
        _isExcludedPath = isExcludedPath;
        _getCachedSubFolders = getCachedSubFolders;
        _isShortcut = isShortcut;
        DisplayName = GetDisplayName(path);
        IconSource = WindowsShellIconProvider.GetFolderSmallIcon();

        // 最初から展開ボタンを表示、ダミーアイテムを追加
        // ファイルシステムアクセスはスキップして UIスレッドをブロックしない
        if (!string.IsNullOrEmpty(path))
        {
            HasSubFolders = true;
            _subFolders = new ObservableCollection<FolderItemViewModel> { CreatePlaceholder() };
        }
    }

    /// <summary>ツリー最上位の仮想ノード（Favorites / Frequently Used / Folders）を生成する。</summary>
    /// <param name="kind">仮想ノードの種類。</param>
    /// <param name="children">子として共有するコレクション（RootFolders本体・お気に入り一覧など）。</param>
    /// <param name="isExpanded">初期状態で展開するか。</param>
    public static FolderItemViewModel CreateVirtualNode(
        VirtualFolderKind kind,
        ObservableCollection<FolderItemViewModel> children,
        bool isExpanded)
    {
        return new FolderItemViewModel(kind, children, isExpanded);
    }

    /// <summary>
    /// 仮想ノード用コンストラクタ。子は渡されたコレクション（RootFolders本体・お気に入り一覧など）を
    /// 共有するため、呼び出し側の差分更新がそのままツリーへ反映される。
    /// 実パスを持たないため遅延読み込みは行わない。
    /// </summary>
    private FolderItemViewModel(VirtualFolderKind kind, ObservableCollection<FolderItemViewModel> children, bool isExpanded)
    {
        _path = VirtualFolders.GetPath(kind) ?? VirtualFolders.AllRootsPath;
        _isExcludedPath = null;
        SetLocalizedDisplayName(VirtualFolders.GetDisplayNameKey(kind));
        GlyphIcon = VirtualFolders.GetGlyph(kind);
        // 記号を持つノード（Favorites等）はアイコンを出さず、記号だけで種類を区別する
        IconSource = GlyphIcon is null ? WindowsShellIconProvider.GetFolderSmallIcon() : null;
        _subFolders = children;
        _isLoaded = true;
        _isExpanded = isExpanded;

        // 子が0件のとき（お気に入り未登録など）に展開ボタンを出さないよう、共有コレクションの増減に追従する
        HasSubFolders = children.Count > 0;
        children.CollectionChanged += (_, _) => HasSubFolders = children.Count > 0;
    }

    /// <summary>
    /// 子フォルダを遅延読み込みする。列挙は必ずバックグラウンドで行う
    /// —— 子が2万を超えるフォルダでは列挙だけでUIスレッドが数百msブロックされるため、
    /// パス遡査のような「すぐ結果が要る」経路も含めて同期版は持たない。
    /// キャッシュDBに子フォルダが載っていれば先にそれを出して返り、ファイルシステムの読み直しは裏で続ける
    /// —— 切断中のNASではファイルシステムの列挙がSMBのタイムアウト（数十秒）まで戻らず、
    /// その間ツリーが「読み込み中...」のまま展開もパスの追従もできなくなるため（ファイル一覧と同じ2段構え）。
    /// </summary>
    public async Task EnsureLoadedAsync()
    {
        if (_isLoaded)
        {
            return;
        }

        _isLoaded = true;
        var version = ++_loadVersion;

        if (_getCachedSubFolders is not null)
        {
            var cachedSubFolders = await Task.Run(GetCachedSubFoldersList);
            if (version != _loadVersion)
            {
                return;
            }

            if (cachedSubFolders.Count > 0)
            {
                await RunOnUiThreadAsync(() => MergeSubFolders(cachedSubFolders));
                BackgroundRefresh = RefreshFromFileSystemAsync(version);
                return;
            }
        }

        await RefreshFromFileSystemAsync(version);
    }

    /// <summary>ファイルシステムから子フォルダを読み直して反映する。読めなければ今の表示（キャッシュ由来）を残す。</summary>
    private async Task RefreshFromFileSystemAsync(int version)
    {
        List<FolderItemViewModel>? subDirs = null;

        // つながらないと分かっているボリュームには問い合わせない
        if (!VolumeAvailabilityTracker.Shared.IsUnreachable(_path))
        {
            // 列挙はスレッドプールを使わない —— 応答しない共有ではSMBのタイムアウトまでスレッドを握るため
            subDirs = await Task.Factory.StartNew(
                GetSubFoldersListOrNull,
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }

        await RunOnUiThreadAsync(() =>
        {
            // 待っている間に Reload された（条件が変わった）なら、古い条件の結果は捨てる
            if (version != _loadVersion)
            {
                return;
            }

            if (subDirs is not null)
            {
                MergeSubFolders(subDirs);
                return;
            }

            // 読めなかった（切断中など）。キャッシュ由来の表示はそのまま残し、次の展開で読み直させる。
            // 何も出せていなければダミーだけ外し、展開ボタンは残す（消すと読み直す手段がなくなる）
            _isLoaded = false;
            if (IsShowingPlaceholder())
            {
                SetSubFolders(Array.Empty<FolderItemViewModel>());
            }
        });
    }

    /// <summary>UIスレッドで処理する（単体テストなど Application が無い場合はそのまま実行する）。</summary>
    private static async Task RunOnUiThreadAsync(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        await dispatcher.InvokeAsync(action);
    }

    /// <summary>
    /// 読み込み済みの子フォルダを捨て、次の展開で読み直す（隠しフォルダの表示切り替えなど、
    /// 列挙の条件が変わったときに使う）。展開中のノードはその場で読み直す。
    /// 子孫の展開状態は保たれない（条件が変わった以上、下位も並び直す必要があるため）。
    /// 子を共有している仮想ノードと、遅延読み込みのダミーは対象外。
    /// </summary>
    public void Reload()
    {
        if (string.IsNullOrEmpty(_path) || VirtualFolders.IsVirtual(_path))
        {
            return;
        }

        _isLoaded = false;
        // 読み込み途中の結果は古い条件で作られているので捨てさせる
        _loadVersion++;

        SetSubFolders(new[] { CreatePlaceholder() });
        HasSubFolders = true;

        if (IsExpanded)
        {
            _ = EnsureLoadedAsync();
        }
    }

    private static FolderItemViewModel CreatePlaceholder()
    {
        var dummy = new FolderItemViewModel(string.Empty, null);
        dummy.SetLocalizedDisplayName(LoadingDisplayNameKey);
        return dummy;
    }

    /// <summary>まだ実際の子を1つも出していない（ダミーだけ、または空）か。</summary>
    private bool IsShowingPlaceholder()
    {
        return _subFolders is null || _subFolders.All(x => string.IsNullOrEmpty(x._path));
    }

    /// <summary>キャッシュDBに載っている直下の子フォルダを、ファイルシステムからの列挙と同じ条件・並びで返す。</summary>
    private List<FolderItemViewModel> GetCachedSubFoldersList()
    {
        try
        {
            var attributesToSkip = AttributesToSkip;
            return _getCachedSubFolders!(_path)
                .Where(entry => entry.Attributes is not { } attributes || ((FileAttributes)attributes & attributesToSkip) == 0)
                .Where(entry => _isExcludedPath?.Invoke(entry.FullPath) != true)
                .OrderBy(entry => entry.Name)
                .Select(entry => CreateChild(entry.FullPath))
                .ToList();
        }
        catch
        {
            // キャッシュが読めなければファイルシステム側に任せる
            return new List<FolderItemViewModel>();
        }
    }

    /// <summary>直下の子フォルダ一覧を取得する。フォルダ自体を読めない場合（切断・アクセス権なし等）は null を返す。</summary>
    private List<FolderItemViewModel>? GetSubFoldersListOrNull()
    {
        try
        {
            var dirInfo = new DirectoryInfo(_path);
            return dirInfo
                .EnumerateDirectories("*", _nonRecursiveEnumerationOptions)
                .Where(d => _isExcludedPath?.Invoke(d.FullName) != true)
                .OrderBy(d => d.Name)
                .Select(d => CreateChild(d.FullName))
                .ToList();
        }
        catch
        {
            // ボリュームごと見えなくなっていれば控え、以後は問い合わせずに済ませる
            VolumeAvailabilityTracker.Shared.ReportFailure(_path);
            return null;
        }
    }

    private FolderItemViewModel CreateChild(string fullPath)
    {
        return new FolderItemViewModel(fullPath, _isExcludedPath, _isShortcut, _getCachedSubFolders);
    }

    /// <summary>
    /// 取得した子フォルダ一覧を反映する（遅延読み込み中のダミーもここで消える）。
    /// 既に出ている子と同じフォルダは既存のノードを使い回す —— キャッシュ由来の表示をファイルシステムの結果で
    /// 置き換えるときに作り直すと、展開済みの孫や展開状態が失われるため。
    /// 差が少なければその場で足し引きし、多ければコレクションごと差し替える（件数分の通知を避ける。<see cref="SetSubFolders"/>）。
    /// </summary>
    private void MergeSubFolders(List<FolderItemViewModel> loaded)
    {
        HasSubFolders = loaded.Count > 0;

        if (IsShowingPlaceholder())
        {
            SetSubFolders(loaded);
            return;
        }

        var current = _subFolders!;
        var currentByPath = new Dictionary<string, FolderItemViewModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in current)
        {
            currentByPath.TryAdd(item._path, item);
        }

        var target = loaded
            .Select(item => currentByPath.GetValueOrDefault(item._path) ?? item)
            .ToList();

        if (target.Count == current.Count && target.Zip(current).All(pair => ReferenceEquals(pair.First, pair.Second)))
        {
            return;
        }

        const int maxInPlaceChanges = 100;
        var targetSet = new HashSet<FolderItemViewModel>(target, ReferenceEqualityComparer.Instance);
        var removedCount = current.Count(item => !targetSet.Contains(item));
        var addedCount = target.Count - (current.Count - removedCount);
        if (removedCount + addedCount > maxInPlaceChanges)
        {
            SetSubFolders(target);
            return;
        }

        for (var i = current.Count - 1; i >= 0; i--)
        {
            if (!targetSet.Contains(current[i]))
            {
                current.RemoveAt(i);
            }
        }

        var kept = new HashSet<FolderItemViewModel>(current, ReferenceEqualityComparer.Instance);
        for (var i = 0; i < target.Count; i++)
        {
            if (i < current.Count && ReferenceEquals(current[i], target[i]))
            {
                continue;
            }

            if (kept.Contains(target[i]))
            {
                // 残した子の並びが入れ替わっている（足し引きでは合わせられない）
                SetSubFolders(target);
                return;
            }

            current.Insert(i, target[i]);
        }
    }

    private static string GetDisplayName(string path)
    {
        var displayName = System.IO.Path.GetFileName(path);
        return string.IsNullOrWhiteSpace(displayName) ? path : displayName;
    }

    /// <summary>対訳表のキーで表示名を設定する（言語切り替え時に引き直せるようキーを控える）。</summary>
    private void SetLocalizedDisplayName(string key)
    {
        _displayNameKey = key;
        DisplayName = UiText.Get(key);
    }

    /// <summary>
    /// 言語切り替え後に、対訳表から引いている表示名（仮想ノード・読み込み中のダミー）を引き直す。
    /// 実フォルダのノードはフォルダ名がそのまま表示名なので何もしない。
    /// </summary>
    public void RefreshLocalizedDisplayName()
    {
        if (_displayNameKey is { } key)
        {
            DisplayName = UiText.Get(key);
        }

        // 未展開のノードがぶら下げているダミー（「読み込み中...」）も辿って更新する
        if (_subFolders is null)
        {
            return;
        }

        foreach (var subFolder in _subFolders)
        {
            subFolder.RefreshLocalizedDisplayName();
        }
    }
}
