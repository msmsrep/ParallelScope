namespace ParallelScope.Data;

/// <summary>settings.json に保存する、1つのペインのタブ構成。</summary>
public sealed class PaneStateSettings
{
    /// <summary>タブの並び（左から順）。</summary>
    public List<TabStateSettings> Tabs { get; set; } = new();

    /// <summary>表示していたタブの位置。</summary>
    public int ActiveTabIndex { get; set; }

    /// <summary>フォルダツリーを開いていたか（Plus機能。持たない既存の設定は開いた状態にする）。</summary>
    public bool IsTreeVisible { get; set; } = true;
}

/// <summary>
/// settings.json に保存する、1つのタブの状態。
/// 検索語と戻る/進む履歴は保存しない（起動時に検索状態から始まると意図が伝わりにくいため）。
/// </summary>
public sealed class TabStateSettings
{
    /// <summary>現在パス（仮想ノードは "::Recent::" のような仮想パス）。</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>そのタブのAll Filesモード。</summary>
    public bool IsFlatFileViewEnabled { get; set; }
}
