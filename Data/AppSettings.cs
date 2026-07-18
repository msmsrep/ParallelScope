namespace ParallelScope.Data;

public sealed class AppSettings
{
    public const int DefaultFullScanIntervalHours = 3;

    public List<string> RootPaths { get; set; } = new();
    public List<string> ExcludedPaths { get; set; } = new();
    public int FullScanIntervalHours { get; set; } = DefaultFullScanIntervalHours;
    public bool IsFlatFileViewEnabled { get; set; }

    /// <summary>ファイル一覧に表示する列のキー一覧（FileListColumns参照）。nullは未設定＝デフォルト列を表示。</summary>
    public List<string>? VisibleColumns { get; set; }
}
