using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ParallelScope.Data;
using ParallelScope.Utilities;

namespace ParallelScope.ViewModels;

/// <summary>2画面（分割表示）の向き。</summary>
public enum PaneSplitOrientation
{
    /// <summary>左右に並べる。</summary>
    Vertical,

    /// <summary>上下に並べる。</summary>
    Horizontal
}

/// <summary>
/// 閲覧ペインの構成（1画面／2画面）に関する処理。
/// ペインは最大2つで、操作対象（メニュー・スキャン・ショートカットの反映先）が <see cref="ActivePane"/>。
/// </summary>
public partial class MainWindowViewModel
{
    /// <summary>分割時の既定の比率（左右・上下とも半々）。</summary>
    public const double DefaultSplitRatio = 0.5;

    private int _activePaneIndex;
    private bool _isSplitViewEnabled;
    private PaneSplitOrientation _splitOrientation = PaneSplitOrientation.Vertical;
    private double _splitRatio = DefaultSplitRatio;

    // 復元は購読状態が分かってからでないと行えないため、それまでは読み込んだ内容をそのまま書き戻す
    // （未購読の間に保存済みのタブ構成が消えないようにする）
    private List<PaneStateSettings>? _savedPaneStates;
    private bool _savedIsSplitViewEnabled;
    private int _savedActivePaneIndex;
    private bool _hasRestoredPanes;

    /// <summary>操作対象のペイン（メニューやスキャンの反映先）。</summary>
    public BrowserPaneViewModel ActivePane => Panes[_activePaneIndex];

    /// <summary>2画面（分割表示）中かどうか。</summary>
    public bool IsSplitViewEnabled => _isSplitViewEnabled;

    /// <summary>分割の向き。</summary>
    public PaneSplitOrientation SplitOrientation => _splitOrientation;

    /// <summary>分割時の1つ目のペインの比率（0.1〜0.9）。</summary>
    public double SplitRatio => _splitRatio;

    /// <summary>保存済みのペイン構成を読み込む（復元自体は購読状態が分かってから行う）。</summary>
    private void LoadPaneStates(AppSettings settings)
    {
        _savedPaneStates = settings.Panes;
        _savedIsSplitViewEnabled = settings.IsSplitViewEnabled;
        _savedActivePaneIndex = settings.ActivePaneIndex;

        // 向きと比率は表示の好みなので、購読状態に関わらず読み込んでおく
        _splitOrientation = Enum.TryParse<PaneSplitOrientation>(settings.SplitOrientation, true, out var orientation)
            ? orientation
            : PaneSplitOrientation.Vertical;
        _splitRatio = settings.SplitRatio is { } ratio && double.IsFinite(ratio)
            ? Math.Clamp(ratio, 0.1, 0.9)
            : DefaultSplitRatio;
    }

    /// <summary>
    /// 保存済みのタブ構成・分割状態を復元する。タブと分割はPlus機能のため、
    /// 未購読の間は復元せず、保存済みの内容にも触れない（購読すれば元の構成に戻る）。
    /// 起動直後は購読状態が未確定なので、確定した時点で1回だけ呼ばれる。
    /// </summary>
    public void RestorePanes(bool arePlusFeaturesEnabled)
    {
        if (_hasRestoredPanes || !arePlusFeaturesEnabled)
        {
            return;
        }

        var states = _savedPaneStates;
        var fallbackPath = _rootPathsSnapshot.FirstOrDefault();

        if (states is { Count: > 0 })
        {
            Panes[0].RestoreTabs(states[0].Tabs, states[0].ActiveTabIndex, fallbackPath);
            Panes[0].InitializeTreeVisible(states[0].IsTreeVisible);

            if (_savedIsSplitViewEnabled && states.Count > 1)
            {
                var secondPane = EnableSplitView();
                secondPane.RestoreTabs(states[1].Tabs, states[1].ActiveTabIndex, fallbackPath);
                secondPane.InitializeTreeVisible(states[1].IsTreeVisible);
            }

            if (_savedActivePaneIndex > 0 && _savedActivePaneIndex < Panes.Count)
            {
                SetActivePane(Panes[_savedActivePaneIndex]);
            }
        }

        // 復元したタブが、表示されていない仮想ノードを開いたままにならないようにする
        LeaveHiddenVirtualFolder();

        _hasRestoredPanes = true;
        SaveSettings();
    }

    /// <summary>タブ構成・分割状態が変わったので保存する（復元前は保存済みの内容をそのまま書き戻す）。</summary>
    internal void OnPaneStateChanged()
    {
        if (!_isInitialized)
        {
            return;
        }

        SaveSettings();
    }

    /// <summary>settings.json へ書き出すペイン構成を組み立てる（復元前は読み込んだ内容をそのまま返す）。</summary>
    private List<PaneStateSettings>? BuildPaneStates()
    {
        return _hasRestoredPanes
            ? Panes.Select(pane => pane.CreateStateSettings()).ToList()
            : _savedPaneStates;
    }

    /// <summary>settings.json へ書き出す「分割中か」（復元前は読み込んだ値のまま）。</summary>
    private bool GetPersistedIsSplitViewEnabled() => _hasRestoredPanes ? _isSplitViewEnabled : _savedIsSplitViewEnabled;

    /// <summary>settings.json へ書き出す操作対象ペインの位置（復元前は読み込んだ値のまま）。</summary>
    private int GetPersistedActivePaneIndex() => _hasRestoredPanes ? _activePaneIndex : _savedActivePaneIndex;

    /// <summary>
    /// ペインを2つに増やす。2つ目は現在のタブと同じ場所を開いた状態で始める。
    /// 横幅が半分になるためツリーは畳んだ状態から始める（保存済み状態からの復元では呼び出し側が上書きする）。
    /// </summary>
    public BrowserPaneViewModel EnableSplitView()
    {
        if (_isSplitViewEnabled)
        {
            return Panes[1];
        }

        var pane = CreatePane();
        pane.ActiveTab.NavigateTo(ActiveTab.CurrentPath, false);
        pane.InitializeTreeVisible(false);
        Panes.Add(pane);

        _isSplitViewEnabled = true;
        OnPropertyChanged(nameof(IsSplitViewEnabled));
        NotifySplitStateChanged();
        OnPaneStateChanged();
        return pane;
    }

    /// <summary>
    /// ペインを1つに戻す。操作対象のペインを残し、閉じる側のタブは残る側の末尾へ移して
    /// 上限を超える分だけ古いものから閉じる（閲覧中の状態をできるだけ失わせない）。
    /// </summary>
    public void DisableSplitView()
    {
        if (!_isSplitViewEnabled)
        {
            return;
        }

        ClosePane(Panes.First(pane => !ReferenceEquals(pane, ActivePane)), moveTabs: true);
    }

    /// <summary>
    /// 指定のペインを閉じて1画面に戻す。<paramref name="moveTabs"/> が真なら閉じる側のタブを
    /// 残る側の末尾へ移し、偽ならそのまま閉じる（最後のタブを閉じてペインごと畳む場合）。
    /// </summary>
    public void ClosePane(BrowserPaneViewModel closedPane, bool moveTabs)
    {
        if (!_isSplitViewEnabled || GetOtherPane(closedPane) is not { } keptPane)
        {
            return;
        }

        if (moveTabs)
        {
            foreach (var tab in closedPane.Tabs.ToList())
            {
                if (!keptPane.CanAddTab)
                {
                    break;
                }

                closedPane.ReleaseTab(tab);
                keptPane.AdoptTab(tab, keptPane.Tabs.Count);
            }
        }

        closedPane.PropertyChanged -= Pane_PropertyChanged;
        Panes.Remove(closedPane);

        _activePaneIndex = 0;
        _isSplitViewEnabled = false;
        keptPane.IsActive = true;
        OnPropertyChanged(nameof(IsSplitViewEnabled));
        OnPropertyChanged(nameof(ActivePane));
        NotifySplitStateChanged();
        RebindActiveTab();
        OnPaneStateChanged();
    }

    /// <summary>分割の向きを切り替える。</summary>
    public void SetSplitOrientation(PaneSplitOrientation orientation)
    {
        if (_splitOrientation == orientation)
        {
            return;
        }

        _splitOrientation = orientation;
        OnPropertyChanged(nameof(SplitOrientation));
        OnPaneStateChanged();
    }

    /// <summary>分割の比率を記録する（スプリッターのドラッグ後に呼ばれる）。</summary>
    public void SetSplitRatio(double ratio)
    {
        if (!double.IsFinite(ratio))
        {
            return;
        }

        _splitRatio = Math.Clamp(ratio, 0.1, 0.9);
        OnPropertyChanged(nameof(SplitRatio));
        OnPaneStateChanged();
    }

    /// <summary>操作対象のペインを切り替える。</summary>
    public void SetActivePane(BrowserPaneViewModel pane)
    {
        var index = Panes.IndexOf(pane);
        if (index < 0 || index == _activePaneIndex)
        {
            return;
        }

        Panes[_activePaneIndex].IsActive = false;
        _activePaneIndex = index;
        pane.IsActive = true;

        OnPropertyChanged(nameof(ActivePane));
        RebindActiveTab();
        OnPaneStateChanged();
    }

    /// <summary>もう一方のペインを返す（分割していなければ null）。</summary>
    public BrowserPaneViewModel? GetOtherPane(BrowserPaneViewModel pane)
    {
        return Panes.FirstOrDefault(x => !ReferenceEquals(x, pane));
    }

    /// <summary>指定のタブが属するペインを返す（見つからなければ null）。</summary>
    public BrowserPaneViewModel? FindPaneOf(BrowserTabViewModel tab)
    {
        return Panes.FirstOrDefault(pane => pane.Tabs.Contains(tab));
    }

    /// <summary>
    /// タブを別のペインへ移す（タブ列をまたぐドラッグ）。閲覧中の状態を保つため、
    /// 新しく作り直さずインスタンスをそのまま移動する。
    /// 移動元が空になる場合と、移動先が上限に達している場合は移動しない。
    /// </summary>
    public bool MoveTabToPane(BrowserTabViewModel tab, BrowserPaneViewModel targetPane, int index)
    {
        var sourcePane = FindPaneOf(tab);
        if (sourcePane is null || ReferenceEquals(sourcePane, targetPane))
        {
            return false;
        }

        if (!sourcePane.CanCloseTabs || !targetPane.CanAddTab)
        {
            return false;
        }

        sourcePane.ReleaseTab(tab);
        targetPane.AdoptTab(tab, index);
        targetPane.ActivateTab(tab);
        SetActivePane(targetPane);
        return true;
    }

    private BrowserPaneViewModel CreatePane()
    {
        var pane = new BrowserPaneViewModel(this);
        pane.PropertyChanged += Pane_PropertyChanged;
        pane.ApplyRootPaths(_rootPathsSnapshot);
        pane.RefreshFavoriteFolders();
        pane.RefreshRecentFolders();
        pane.RefreshFrequentFolders();
        pane.RebuildTreeRoots();
        return pane;
    }

    // 表示中のタブが切り替わったら、中継先を繋ぎ替えて委譲プロパティをまとめて通知し直す
    private void Pane_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(BrowserPaneViewModel.ActiveTab)
            || !ReferenceEquals(sender, ActivePane))
        {
            return;
        }

        RebindActiveTab();
    }

    // アクティブなタブが入れ替わったときに、変更通知の中継先を繋ぎ替えて委譲プロパティを通知し直す
    private void RebindActiveTab()
    {
        _observedTab.PropertyChanged -= ActiveTab_PropertyChanged;
        _observedTab = ActiveTab;
        _observedTab.PropertyChanged += ActiveTab_PropertyChanged;

        OnPropertyChanged(nameof(ActiveTab));
        OnPropertyChanged(nameof(RootFolders));
        OnPropertyChanged(nameof(TreeRoots));
        OnPropertyChanged(nameof(AllRootsNode));
        OnPropertyChanged(nameof(FileItems));
        OnPropertyChanged(nameof(CurrentPath));
        OnPropertyChanged(nameof(AddressInput));
        OnPropertyChanged(nameof(SearchQuery));
        OnPropertyChanged(nameof(IsFlatFileViewEnabled));
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        OnPropertyChanged(nameof(CanGoUp));
    }

    // アクティブペインの枠線は分割中だけ出すため、分割の切り替えでも通知し直す
    private void NotifySplitStateChanged()
    {
        foreach (var pane in Panes)
        {
            pane.NotifySplitStateChanged();
        }
    }
}
