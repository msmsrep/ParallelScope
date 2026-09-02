namespace ParallelScope.ViewModels;

/// <summary>
/// 閉じたタブを開き直す（Ctrl+Shift+T）ために控える状態。
/// アプリ実行中のメモリ上にだけ持ち、settings.json には保存しない。
/// </summary>
/// <param name="Path">閉じた時点の現在パス。</param>
/// <param name="IsFlatFileViewEnabled">閉じた時点のAll Filesモード。</param>
/// <param name="BackHistory">戻る履歴（新しい順）。</param>
/// <param name="ForwardHistory">進む履歴（新しい順）。</param>
/// <param name="Index">タブ列での位置（同じ場所へ挿し戻すため）。</param>
internal sealed record ClosedTabState(
    string Path,
    bool IsFlatFileViewEnabled,
    IReadOnlyList<string> BackHistory,
    IReadOnlyList<string> ForwardHistory,
    int Index);
