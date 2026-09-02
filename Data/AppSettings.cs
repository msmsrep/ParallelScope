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

    /// <summary>ファイル一覧の列の並び順（列キー。Nameを含む）。nullは未設定＝既定の並び順。</summary>
    public List<string>? ColumnOrder { get; set; }

    /// <summary>ツリー最上位に表示するノードのキー一覧（TreeNodes参照）。nullは未設定＝既定のノードを表示。</summary>
    public List<string>? VisibleTreeNodes { get; set; }

    /// <summary>ツリー最上位のノードの並び順（ノードキー。常に表示のFoldersを含む）。nullは未設定＝既定の並び順。</summary>
    public List<string>? TreeNodeOrder { get; set; }

    /// <summary>
    /// ファイル一覧の列幅（列キー→ピクセル幅）。
    /// 未収録の列は既定の幅で表示する（残り幅いっぱいのName列は、ユーザーが幅を変えるまで収録されない）。
    /// </summary>
    public Dictionary<string, double>? ColumnWidths { get; set; }

    /// <summary>CSV出力でSize列を生のバイト数で書き出すか（falseなら "11.8 MB" のような表示中の文字列）。</summary>
    public bool CsvExportSizeInBytes { get; set; }

    /// <summary>
    /// 隠し属性のファイル/フォルダを一覧・ツリーに出すか。
    /// 既定は表示（この設定を入れる前と同じ見え方。設定の無い settings.json からもtrueで読み込まれる）。
    /// </summary>
    public bool ShowHiddenItems { get; set; } = true;

    /// <summary>システム属性のファイル/フォルダを一覧・ツリーに出すか。既定は <see cref="ShowHiddenItems"/> と同じ理由で表示。</summary>
    public bool ShowSystemItems { get; set; } = true;

    /// <summary>お気に入りに登録されたフォルダのパス一覧（登録順）。</summary>
    public List<string> FavoritePaths { get; set; } = new();

    /// <summary>「Frequently Used」の並び順を決めるフォルダごとのアクセス実績。</summary>
    public List<FolderUsageEntry> FolderUsages { get; set; } = new();

    /// <summary>配色テーマ（AppThemeSettingの名前）。nullは未設定＝Windowsの設定に追従。</summary>
    public string? Theme { get; set; }

    /// <summary>表示言語（AppLanguageSettingの名前）。nullは未設定＝Windowsの表示言語に追従。</summary>
    public string? Language { get; set; }

    /// <summary>2画面（分割表示）が有効か。</summary>
    public bool IsSplitViewEnabled { get; set; }

    /// <summary>分割の向き（PaneSplitOrientationの名前）。nullは未設定＝左右に分割。</summary>
    public string? SplitOrientation { get; set; }

    /// <summary>分割時の1つ目のペインの比率（0.1〜0.9）。nullは未設定＝半々。</summary>
    public double? SplitRatio { get; set; }

    /// <summary>表示していたペインの位置（0 or 1）。</summary>
    public int ActivePaneIndex { get; set; }

    /// <summary>
    /// ペインごとのタブ構成（要素0が左/上、1が右/下）。nullは未設定＝1ペイン1タブ。
    /// タブと分割はPlus機能のため、未購読の間は復元せず、保存済みの内容もそのまま残す。
    /// </summary>
    public List<PaneStateSettings>? Panes { get; set; }

    /// <summary>
    /// 開発者専用: Plus機能をStoreの購読なしで解放するためのキー。通常はnull。
    /// 正しいキーかどうかはStoreLicenseServiceが埋め込みハッシュとの照合で判定する。
    /// </summary>
    public string? DeveloperUnlockKey { get; set; }
}
