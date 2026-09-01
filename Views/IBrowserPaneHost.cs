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
}
