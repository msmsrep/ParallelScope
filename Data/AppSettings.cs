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

    /// <summary>配色テーマ（AppThemeSettingの名前）。nullは未設定＝Windowsの設定に追従。</summary>
    public string? Theme { get; set; }

    /// <summary>
    /// 開発者専用: Plus機能をStoreの購読なしで解放するためのキー。通常はnull。
    /// 正しいキーかどうかはStoreLicenseServiceが埋め込みハッシュとの照合で判定する。
    /// </summary>
    public string? DeveloperUnlockKey { get; set; }
}
