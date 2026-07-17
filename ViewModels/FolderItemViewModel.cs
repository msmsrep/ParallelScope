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

    private readonly string _path;
    private readonly Func<string, bool>? _isExcludedPath;
    private ObservableCollection<FolderItemViewModel>? _subFolders;
    private bool _isScanning;
    private bool _isLoaded;
    private bool _hasSubFolders = true;
    private bool _isExpanded;
    private ImageSource? _iconSource;

    public string DisplayName { get; set; }

    public string Path => _path;

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

    /// <summary>ツリー上の展開状態。仮想「Roots」ノードを既定で展開表示するために持つ。</summary>
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

    public FolderItemViewModel(string path, Func<string, bool>? isExcludedPath = null)
    {
        _path = path;
        _isExcludedPath = isExcludedPath;
        DisplayName = GetDisplayName(path);
        IconSource = WindowsShellIconProvider.GetFolderSmallIcon();

        // 最初から展開ボタンを表示、ダミーアイテムを追加
        // ファイルシステムアクセスはスキップして UIスレッドをブロックしない
        if (!string.IsNullOrEmpty(path))
        {
            HasSubFolders = true;
            _subFolders = new ObservableCollection<FolderItemViewModel>();
            var dummy = new FolderItemViewModel(string.Empty, null);
            dummy.DisplayName = "読み込み中...";
            _subFolders.Add(dummy);
        }
    }

    /// <summary>全ルートフォルダを子として表示する、ツリー最上位の仮想「Roots」ノードを生成する。</summary>
    public static FolderItemViewModel CreateAllRootsNode(ObservableCollection<FolderItemViewModel> rootFolders)
    {
        return new FolderItemViewModel(rootFolders);
    }

    /// <summary>
    /// 仮想「Roots」ノード用コンストラクタ。子は渡されたコレクション（RootFolders本体）を共有するため、
    /// ルート設定の差分更新がそのままツリーへ反映される。実パスを持たないため遅延読み込みは行わない。
    /// </summary>
    private FolderItemViewModel(ObservableCollection<FolderItemViewModel> subFolders)
    {
        _path = AllRootsVirtualFolder.Path;
        _isExcludedPath = null;
        DisplayName = AllRootsVirtualFolder.DisplayName;
        IconSource = WindowsShellIconProvider.GetFolderSmallIcon();
        _subFolders = subFolders;
        _isLoaded = true;
        _isExpanded = true;
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
                .Select(d => new FolderItemViewModel(d.FullName, _isExcludedPath))
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
}
