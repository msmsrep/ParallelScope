using CommunityToolkit.Mvvm.ComponentModel;

namespace ParallelScope.ViewModels;

public class FileItemViewModel : ObservableObject
{
    private static readonly string[] SizeUnits = { "B", "KB", "MB", "GB", "TB" };

    private string _fullPath = string.Empty;
    private string _name = string.Empty;
    private string _sizeText = string.Empty;
    private string _typeText = string.Empty;
    private string _modifiedTime = string.Empty;
    private bool _isFolder;

    public string FullPath
    {
        get => _fullPath;
        set => SetProperty(ref _fullPath, value);
    }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string SizeText
    {
        get => _sizeText;
        set => SetProperty(ref _sizeText, value);
    }

    public string TypeText
    {
        get => _typeText;
        set => SetProperty(ref _typeText, value);
    }

    public string ModifiedTime
    {
        get => _modifiedTime;
        set => SetProperty(ref _modifiedTime, value);
    }

    public bool IsFolder
    {
        get => _isFolder;
        set => SetProperty(ref _isFolder, value);
    }

    /// <summary>
    /// フォルダサイズのバイト数（再描画判定用）。SizeText の値が同じでもバイト数で正確に比較。
    /// </summary>
    public long CachedSizeBytes { get; set; }

    public FileItemViewModel(string fullPath, string name, long sizeBytes, DateTime modifiedTime)
    {
        FullPath = fullPath;
        Name = name;
        SizeText = FormatFileSize(sizeBytes);
        TypeText = "File";
        ModifiedTime = modifiedTime.ToString("yyyy-MM-dd HH:mm:ss");
        IsFolder = false;
        CachedSizeBytes = sizeBytes;
    }

    public FileItemViewModel(string fullPath, string name, DateTime modifiedTime, long? cachedTotalSizeBytes = null)
    {
        FullPath = fullPath;
        Name = name;
        SizeText = cachedTotalSizeBytes.HasValue ? FormatFileSize(cachedTotalSizeBytes.Value) : string.Empty;
        TypeText = "Folder";
        ModifiedTime = modifiedTime.ToString("yyyy-MM-dd HH:mm:ss");
        IsFolder = true;
        CachedSizeBytes = cachedTotalSizeBytes ?? 0;
    }

    private static string FormatFileSize(long bytes)
    {
        double len = bytes;
        int order = 0;
        int maxOrder = SizeUnits.Length - 1;

        while (len >= 1024 && order < maxOrder)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {SizeUnits[order]}";
    }
}
