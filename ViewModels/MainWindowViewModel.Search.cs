namespace ParallelScope.ViewModels;

/// <summary>
/// 検索の照合方法（正規表現。Plus機能）の設定。
/// 実際に使うかは設定と購読状態の両方で決まり、タブは <see cref="IBrowserTabHost.UseRegexSearch"/> 経由で参照する。
/// </summary>
public partial class MainWindowViewModel
{
    /// <summary>設定画面で「正規表現で検索する」が選ばれているか（購読状態は問わない）。</summary>
    private bool _isRegexSearchEnabled;

    /// <summary>正規表現検索の設定が有効か（購読状態は問わない。設定画面へ返す値）。</summary>
    public bool GetRegexSearchEnabled() => _isRegexSearchEnabled;

    /// <summary>実際に正規表現として扱ってよい状態か（設定と購読状態の両方が揃っているか）。</summary>
    internal bool IsRegexSearchActive => _isRegexSearchEnabled && _arePlusFeaturesEnabled;

    /// <summary>設定画面からの切り替えを反映して保存する。</summary>
    public void SetRegexSearchEnabled(bool isEnabled)
    {
        if (_isRegexSearchEnabled == isEnabled)
        {
            return;
        }

        _isRegexSearchEnabled = isEnabled;
        NotifySearchModeChanged();
        SaveSettings();
    }

    /// <summary>
    /// 照合方法が変わったことを全タブへ知らせる。
    /// 同じ検索語でも結果が変わるため、打ちながらの絞り込み用に控えた結果は捨てさせて検索し直させる。
    /// </summary>
    private void NotifySearchModeChanged()
    {
        foreach (var tab in AllTabs)
        {
            tab.OnSearchModeChanged();
        }
    }
}
