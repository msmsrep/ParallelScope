using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using ParallelScope.Utilities;

namespace ParallelScope.ViewModels;

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

        // 初期化時にサブフォルダの存在確認（展開ボタン表示用）
        CheckHasSubFolders();
    }

    // 遅延読み込み: TreeViewItemが展開される時に呼ぶ
    public void EnsureLoaded()
    {
        if (_isLoaded)
        {
            return;
        }

        _isLoaded = true;
        LoadSubFolders();
    }

    // サブフォルダの存在確認（ダミーではなく実際に読み込む）
    private void CheckHasSubFolders()
    {
        try
        {
            var dirInfo = new DirectoryInfo(_path);
            var subDirsExist = dirInfo
                .EnumerateDirectories("*", NonRecursiveEnumerationOptions)
                .Any(d => _isExcludedPath?.Invoke(d.FullName) != true);

            HasSubFolders = subDirsExist;

            // サブフォルダがある場合、ダミーアイテムを追加して展開ボタンを表示させる
            if (subDirsExist)
            {
                _subFolders ??= new ObservableCollection<FolderItemViewModel>();
                if (_subFolders.Count == 0)
                {
                    // ダミーアイテムを追加（展開時に実際のアイテムに置き換える）
                    var dummy = new FolderItemViewModel(string.Empty, null);
                    dummy.DisplayName = "読み込み中...";
                    _subFolders.Add(dummy);
                }
            }
        }
        catch
        {
            HasSubFolders = false;
        }
    }

    private static string GetDisplayName(string path)
    {
        var displayName = System.IO.Path.GetFileName(path);
        return string.IsNullOrWhiteSpace(displayName) ? path : displayName;
    }

    private void LoadSubFolders()
    {
        try
        {
            var dirInfo = new DirectoryInfo(_path);
            var subDirs = dirInfo
                .EnumerateDirectories("*", NonRecursiveEnumerationOptions)
                .Where(d => _isExcludedPath?.Invoke(d.FullName) != true)
                .OrderBy(d => d.Name)
                .Select(d => new FolderItemViewModel(d.FullName, _isExcludedPath))
                .ToList();

            // ダミーアイテムがあればクリア
            _subFolders?.Clear();
            _subFolders ??= new ObservableCollection<FolderItemViewModel>();

            foreach (var subDir in subDirs)
            {
                _subFolders.Add(subDir);
            }

            // サブフォルダがない場合、展開ボタンを表示しない
            HasSubFolders = subDirs.Count > 0;
        }
        catch
        {
            // アクセス権限がない場合などはスキップ
            _subFolders?.Clear();
            HasSubFolders = false;
        }
    }
}
