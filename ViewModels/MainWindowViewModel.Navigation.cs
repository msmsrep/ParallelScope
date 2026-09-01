namespace ParallelScope.ViewModels;

/// <summary>
/// フォルダ間ナビゲーションの公開API。実際の処理はアクティブなタブ
/// （<see cref="BrowserTabViewModel"/>）が持ち、ここはその委譲だけを行う。
/// </summary>
public partial class MainWindowViewModel
{
    /// <summary>指定フォルダへ移動する（履歴に追加される）。</summary>
    public bool LoadFiles(string folderPath) => ActiveTab.LoadFiles(folderPath);

    /// <summary>戻る履歴の1つ前のフォルダへ移動する。</summary>
    public bool GoBack() => ActiveTab.GoBack();

    /// <summary>進む履歴の1つ先のフォルダへ移動する。</summary>
    public bool GoForward() => ActiveTab.GoForward();

    /// <summary>親フォルダへ移動する。</summary>
    public bool GoUp() => ActiveTab.GoUp();

    /// <summary>アドレス欄に入力されたパスへ移動する。</summary>
    public bool TryNavigateByAddressInput() => ActiveTab.TryNavigateByAddressInput();

    /// <summary>指定フォルダへ移動する。addToHistoryがtrueなら戻る履歴に現在地を積む。</summary>
    public bool NavigateTo(string folderPath, bool addToHistory) => ActiveTab.NavigateTo(folderPath, addToHistory);

    /// <summary>
    /// 現在表示中のフォルダの一覧を再読み込みする（スキャン完了後にキャッシュの最新内容を反映するため）。
    /// NavigateTo は同一パスへの移動を早期returnで無視するため、再読み込みには使えない。
    /// </summary>
    public void RefreshCurrentFolder() => ActiveTab.RefreshCurrentFolder();
}
