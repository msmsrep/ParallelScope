using ParallelScope.Data;

namespace ParallelScope.Tests.TestSupport;

/// <summary>キャッシュDBへ入れるエントリの組み立て。キャッシュ層はパス文字列を扱うだけなので、パスは実在しなくてよい。</summary>
public static class CacheEntries
{
    public static readonly DateTime ModifiedUtc = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

    public static CachedFileSystemEntry File(
        string parentPath,
        string name,
        long sizeBytes = 100,
        DateTime? creationTimeUtc = null,
        int attributes = 32)
    {
        return new CachedFileSystemEntry(
            parentPath,
            Path.Combine(parentPath, name),
            name,
            IsFolder: false,
            sizeBytes,
            ModifiedUtc,
            creationTimeUtc,
            attributes);
    }

    public static CachedFileSystemEntry Folder(string parentPath, string name)
    {
        return new CachedFileSystemEntry(
            parentPath,
            Path.Combine(parentPath, name),
            name,
            IsFolder: true,
            SizeBytes: null,
            ModifiedUtc,
            CreationTimeUtc: null,
            Attributes: 16);
    }

    /// <summary><c>file000000.txt</c> から連番で並ぶファイル（サイズは連番と同じ）。</summary>
    public static List<CachedFileSystemEntry> NumberedFiles(string parentPath, int count)
    {
        return Enumerable.Range(0, count)
            .Select(i => File(parentPath, $"file{i:D6}.txt", sizeBytes: i))
            .ToList();
    }
}
