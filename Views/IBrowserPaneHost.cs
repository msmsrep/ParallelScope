using ParallelScope.ViewModels;

namespace ParallelScope.Views;

/// <summary>
/// ペイン（<see cref="BrowserPaneView"/>）から、ウィンドウ全体でしか行えない操作を依頼するための窓口。
/// 実装は <see cref="MainWindow"/>。スキャンはアプリ全体で1本に統合されているため、
/// ペイン側で直接動かさずここへ委ねる。
/// </summary>
internal interface IBrowserPaneHost
{
    /// <summary>ツリーのコンテキストメニューから要求された、フォルダ配下の個別スキャンを実行する。</summary>
    Task RunFolderScanAsync(FolderItemViewModel folderItem);

    /// <summary>このペインが操作されたこと（クリック・フォーカス移動）を通知する。</summary>
    void OnPaneActivated(BrowserPaneView pane);

    /// <summary>指定のペインを操作対象にする（反対側のペインで開いた直後など）。</summary>
    void ActivatePane(BrowserPaneViewModel paneViewModel);

    /// <summary>
    /// 1画面のときに画面を分割し、2つ目のペインで指定パスを開いた状態にして返す
    /// （分割はPlus機能のため、未購読の場合や既に分割中の場合は null）。
    /// </summary>
    BrowserPaneViewModel? OpenSplitViewPane(string path);

    /// <summary>
    /// 2画面のときに、指定のペインを閉じて1画面に戻す。
    /// <paramref name="moveTabs"/> が真なら閉じる側のタブを残る側の末尾へ移し、
    /// 偽ならそのまま閉じる（最後のタブを閉じてペインごと畳む場合）。
    /// </summary>
    void ClosePane(BrowserPaneViewModel paneViewModel, bool moveTabs);
}
