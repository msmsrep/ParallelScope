using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ParallelFiler.ViewModels;

public class MainWindowViewModel : ObservableObject
{
    private ObservableCollection<FolderItemViewModel> _rootFolders;
    private ObservableCollection<FileItemViewModel> _fileItems;
    private string _currentPath = string.Empty;

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
        set => SetProperty(ref _currentPath, value);
    }

    public MainWindowViewModel()
    {
        _rootFolders = new ObservableCollection<FolderItemViewModel>();
        _fileItems = new ObservableCollection<FileItemViewModel>();
        
        InitializeRootFolders();
    }

    private void InitializeRootFolders()
    {
        // ドライブをルートとして表示
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.IsReady)
            {
                RootFolders.Add(new FolderItemViewModel(drive.RootDirectory.FullName));
            }
        }
    }

    public void LoadFiles(string folderPath)
    {
        CurrentPath = folderPath;
        FileItems.Clear();

        try
        {
            var dirInfo = new DirectoryInfo(folderPath);
            var files = dirInfo.GetFiles()
                .OrderBy(f => f.Name)
                .Select(f => new FileItemViewModel(f.Name, f.Length, f.LastWriteTime));

            foreach (var file in files)
            {
                FileItems.Add(file);
            }
        }
        catch (UnauthorizedAccessException)
        {
            CurrentPath += " (アクセス権限がありません)";
        }
        catch (Exception ex)
        {
            CurrentPath += $" (エラー: {ex.Message})";
        }
    }
}
