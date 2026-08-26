namespace ParallelScope.Data;

/// <summary>
/// 「Frequently Used」ノードの並びを決めるための、フォルダ1件分のアクセス実績。
/// フォルダへ移動するたびに更新され、settings.json に保存される。
/// </summary>
public sealed class FolderUsageEntry
{
    /// <summary>対象フォルダの正規化済みパス。</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>移動した回数。</summary>
    public int Count { get; set; }

    /// <summary>最後に移動した日時（同数のフォルダ同士の並び順に使う）。</summary>
    public DateTime LastAccessedAt { get; set; }
}
