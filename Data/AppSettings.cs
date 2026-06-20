namespace ParallelScope.Data;

public sealed class AppSettings
{
    public const int DefaultFullScanIntervalHours = 3;

    public List<string> RootPaths { get; set; } = new();
    public List<string> ExcludedPaths { get; set; } = new();
    public int FullScanIntervalHours { get; set; } = DefaultFullScanIntervalHours;
}
