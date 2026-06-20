namespace ParallelScope.ViewModels;

public class FileItemViewModel
{
    public string FullPath { get; set; }
    public string Name { get; set; }
    public string SizeText { get; set; }
    public string TypeText { get; set; }
    public string ModifiedTime { get; set; }
    public bool IsFolder { get; set; }

    public FileItemViewModel(string fullPath, string name, long sizeBytes, DateTime modifiedTime)
    {
        FullPath = fullPath;
        Name = name;
        SizeText = FormatFileSize(sizeBytes);
        TypeText = "File";
        ModifiedTime = modifiedTime.ToString("yyyy-MM-dd HH:mm:ss");
        IsFolder = false;
    }

    public FileItemViewModel(string fullPath, string name, DateTime modifiedTime, long? cachedTotalSizeBytes = null)
    {
        FullPath = fullPath;
        Name = name;
        SizeText = cachedTotalSizeBytes.HasValue ? FormatFileSize(cachedTotalSizeBytes.Value) : string.Empty;
        TypeText = "Folder";
        ModifiedTime = modifiedTime.ToString("yyyy-MM-dd HH:mm:ss");
        IsFolder = true;
    }

    private static string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
}
