namespace ParallelFiler.Data;

public class FileSystemEntryEntity
{
    public int Id { get; set; }
    public string ParentPath { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsFolder { get; set; }
    public long? SizeBytes { get; set; }
    public DateTime LastWriteTimeUtc { get; set; }
}
