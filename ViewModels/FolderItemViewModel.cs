using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

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

    public string DisplayName { get; }

    public string Path => _path;

    public bool IsScanning
    {
        get => _isScanning;
        set => SetProperty(ref _isScanning, value);
    }

    public ObservableCollection<FolderItemViewModel> SubFolders
    {
        get
        {
            _subFolders ??= new ObservableCollection<FolderItemViewModel>();

            // 初回のみサブフォルダを読み込む
            if (_subFolders.Count == 0)
            {
                LoadSubFolders();
            }

            return _subFolders;
        }
    }

    public FolderItemViewModel(string path, Func<string, bool>? isExcludedPath = null)
    {
        _path = path;
        _isExcludedPath = isExcludedPath;
        DisplayName = GetDisplayName(path);
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
                .Select(d => new FolderItemViewModel(d.FullName, _isExcludedPath));

            foreach (var subDir in subDirs)
            {
                _subFolders!.Add(subDir);
            }
        }
        catch
        {
            // アクセス権限がない場合などはスキップ
        }
    }
}
