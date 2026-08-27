using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using ParallelScope.Utilities;

namespace ParallelScope.ViewModels;

/// <summary>フォルダツリーの1ノードを表すViewModel。子フォルダは展開時に遅延読み込みされる。</summary>
public class FolderItemViewModel : ObservableObject
{
    private static readonly EnumerationOptions NonRecursiveEnumerationOptions = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint
    };

    // 遅延読み込み中のダミーノードの表示名（対訳表のキー）
    private const string LoadingDisplayNameKey = "Tree.Loading";

    private readonly string _path;
    private readonly Func<string, bool>? _isExcludedPath;
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

    public ObservableCollection<FolderItemViewModel> SubFolders
    {
        get
        {
            _subFolders ??= new ObservableCollection<FolderItemViewModel>();
            return _subFolders;
        }
    }

    public FolderItemViewModel(string path, Func<string, bool>? isExcludedPath = null, bool isShortcut = false)
    {
        _path = path;
        _isExcludedPath = isExcludedPath;
        _isShortcut = isShortcut;
        DisplayName = GetDisplayName(path);
        IconSource = WindowsShellIconProvider.GetFolderSmallIcon();

        // 最初から展開ボタンを表示、ダミーアイテムを追加
        // ファイルシステムアクセスはスキップして UIスレッドをブロックしない
        if (!string.IsNullOrEmpty(path))
        {
            HasSubFolders = true;
            _subFolders = new ObservableCollection<FolderItemViewModel>();
            var dummy = new FolderItemViewModel(string.Empty, null);
            dummy.SetLocalizedDisplayName(LoadingDisplayNameKey);
            _subFolders.Add(dummy);
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
        _path = kind switch
        {
            VirtualFolderKind.Favorites => VirtualFolders.FavoritesPath,
            VirtualFolderKind.Frequent => VirtualFolders.FrequentPath,
            _ => VirtualFolders.AllRootsPath
        };
        _isExcludedPath = null;
        SetLocalizedDisplayName(VirtualFolders.GetDisplayNameKey(kind));
        IconSource = WindowsShellIconProvider.GetFolderSmallIcon();
        _subFolders = children;
        _isLoaded = true;
        _isExpanded = isExpanded;

        // 子が0件のとき（お気に入り未登録など）に展開ボタンを出さないよう、共有コレクションの増減に追従する
        HasSubFolders = children.Count > 0;
        children.CollectionChanged += (_, _) => HasSubFolders = children.Count > 0;
    }

    /// <summary>遅延読み込み（同期版）: パス遡査などで即座に実行が必要な場合に使用。</summary>
    public void EnsureLoaded()
    {
        if (_isLoaded)
        {
            return;
        }

        _isLoaded = true;
        var subDirs = GetSubFoldersList();
        ApplySubFolders(subDirs);
    }

    /// <summary>遅延読み込み（非同期版）: UIスレッドブロックを避ける必要があるイベントで使用。</summary>
    public async Task EnsureLoadedAsync()
    {
        if (_isLoaded)
        {
            return;
        }

        _isLoaded = true;

        // バックグラウンドスレッドで子フォルダリストを構築
        var subDirs = await Task.Run(GetSubFoldersList);

        // UIスレッドに戻ってコレクションを更新
        await Application.Current.Dispatcher.InvokeAsync(() => ApplySubFolders(subDirs));
    }

    /// <summary>直下の子フォルダ一覧を取得する。アクセス不可などの場合は空リストを返す。</summary>
    private List<FolderItemViewModel> GetSubFoldersList()
    {
        try
        {
            var dirInfo = new DirectoryInfo(_path);
            return dirInfo
                .EnumerateDirectories("*", NonRecursiveEnumerationOptions)
                .Where(d => _isExcludedPath?.Invoke(d.FullName) != true)
                .OrderBy(d => d.Name)
                .Select(d => new FolderItemViewModel(d.FullName, _isExcludedPath, _isShortcut))
                .ToList();
        }
        catch
        {
            // アクセス権限がない場合などはスキップ
            return new List<FolderItemViewModel>();
        }
    }

    /// <summary>取得した子フォルダ一覧をコレクションへ反映する（ダミーアイテムのクリアを含む）。</summary>
    private void ApplySubFolders(List<FolderItemViewModel> subDirs)
    {
        _subFolders?.Clear();
        _subFolders ??= new ObservableCollection<FolderItemViewModel>();

        foreach (var subDir in subDirs)
        {
            _subFolders.Add(subDir);
        }

        // サブフォルダがない場合、展開ボタンを表示しない
        HasSubFolders = subDirs.Count > 0;
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
