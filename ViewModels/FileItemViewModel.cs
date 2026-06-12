namespace ParallelFiler.ViewModels;

public class FileItemViewModel
{
    public string Name { get; set; }
    public string SizeText { get; set; }
    public string ModifiedTime { get; set; }

    public FileItemViewModel(string name, long sizeBytes, DateTime modifiedTime)
    {
        Name = name;
        SizeText = FormatFileSize(sizeBytes);
        ModifiedTime = modifiedTime.ToString("yyyy-MM-dd HH:mm:ss");
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
