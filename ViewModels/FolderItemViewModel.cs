using System.Collections.ObjectModel;
using System.IO;

namespace ParallelFiler.ViewModels;

public class FolderItemViewModel
{
    private static readonly EnumerationOptions NonRecursiveEnumerationOptions = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint
    };

    private readonly string _path;
    private ObservableCollection<FolderItemViewModel>? _subFolders;

    public string DisplayName { get; }

    public string Path => _path;

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

    public FolderItemViewModel(string path)
    {
        _path = path;
        DisplayName = System.IO.Path.GetFileName(path) ?? path;
    }

    private void LoadSubFolders()
    {
        try
        {
            var dirInfo = new DirectoryInfo(_path);
            var subDirs = dirInfo
                .EnumerateDirectories("*", NonRecursiveEnumerationOptions)
                .OrderBy(d => d.Name)
                .Select(d => new FolderItemViewModel(d.FullName));

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
