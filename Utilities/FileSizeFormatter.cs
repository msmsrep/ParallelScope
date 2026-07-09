namespace ParallelScope.Utilities;

/// <summary>バイト数を人間が読みやすいサイズ表記（B/KB/MB/GB/TB）に変換するユーティリティ。</summary>
public static class FileSizeFormatter
{
    private static readonly string[] SizeUnits = { "B", "KB", "MB", "GB", "TB" };

    public static string Format(long bytes)
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
