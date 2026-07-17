namespace ParallelScope.Data;

public class FileSystemEntryEntity
{
    public int Id { get; set; }
    public string ParentPath { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsFolder { get; set; }
    public long? SizeBytes { get; set; }
    public DateTime LastWriteTimeUtc { get; set; }

    // 既存キャッシュ行には値が無い（次のスキャンで埋まる）ため、どちらもnull許容にする
    public DateTime? CreationTimeUtc { get; set; }
    public int? Attributes { get; set; }
}
