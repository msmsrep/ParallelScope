using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ParallelScope.Data;

namespace ParallelScope.ViewModels;

/// <summary>
/// 1つの閲覧ペイン（フォルダツリー＋タブ列＋ファイル一覧）のViewModel。
/// タブの開閉・切り替え・並べ替えと、そのペインのフォルダツリーを受け持つ。
/// ツリーのノードは <c>BrowserPaneViewModel.Tree.cs</c> にあり、元データ（ルートパス・
/// お気に入り・アクセス実績）はアプリ全体で1つなのでシェルから借りる。
/// </summary>
public partial class BrowserPaneViewModel : ObservableObject
{
    /// <summary>1ペインに開けるタブの上限。タブごとに一覧を持つためメモリの上限にもなる。</summary>
    public const int MaxTabCount = 20;

    /// <summary>「閉じたタブを開き直す」で遡れる件数。</summary>
    private const int MaxClosedTabHistory = 10;

    private readonly MainWindowViewModel _shell;
    private readonly Stack<ClosedTabState> _closedTabs = new();
    private BrowserTabViewModel _activeTab;
    private bool _isActive;
    private bool _isTreeVisible = true;

    internal BrowserPaneViewModel(MainWindowViewModel shell)
    {
        _shell = shell;

        _activeTab = new BrowserTabViewModel(shell);
        _activeTab.IsActive = true;
        Tabs.Add(_activeTab);
        Tabs.CollectionChanged += (_, _) =>
        {
            NotifyTabCountChanged();
            _shell.OnPaneStateChanged();
        };

        InitializeTreeNodes();
    }

    /// <summary>このペインに開いているタブ（左から並び順どおり）。</summary>
    public ObservableCollection<BrowserTabViewModel> Tabs { get; } = new();

    /// <summary>表示中のタブ。</summary>
    public BrowserTabViewModel ActiveTab
    {
        get => _activeTab;
        private set => SetProperty(ref _activeTab, value);
    }

    /// <summary>
    /// フォルダツリーを開いているか（Plus機能）。未購読の間は畳めないため、
    /// 表示側（<c>BrowserPaneView</c>）がこの値を無視して常に開いた状態にする。
    /// </summary>
    public bool IsTreeVisible
    {
        get => _isTreeVisible;
        set
        {
            if (SetProperty(ref _isTreeVisible, value))
            {
                _shell.OnPaneStateChanged();
            }
        }
    }

    /// <summary>復元時の初期値として入れる（セッターと違い保存を走らせない）。</summary>
    internal void InitializeTreeVisible(bool isVisible)
    {
        if (_isTreeVisible != isVisible)
        {
            _isTreeVisible = isVisible;
            OnPropertyChanged(nameof(IsTreeVisible));
        }
    }

    /// <summary>このペインが操作対象か（メニュー・ショートカットの反映先）。</summary>
    public bool IsActive
    {
        get => _isActive;
        internal set
        {
            if (SetProperty(ref _isActive, value))
            {
                OnPropertyChanged(nameof(ShowsActiveHighlight));
            }
        }
    }

    /// <summary>アクティブなペインである印（枠線）を出すか。1画面のときはどちらでもないので出さない。</summary>
    public bool ShowsActiveHighlight => _isActive && _shell.IsSplitViewEnabled;

    /// <summary>
    /// タブの✕ボタンを出すか。2画面のときは最後の1つも閉じられる
    /// （閉じるとそのペインごと畳んで1画面に戻る）。
    /// </summary>
    public bool ShowsTabCloseButton => CanCloseTabs || _shell.IsSplitViewEnabled;

    /// <summary>分割の切り替えで変わる表示（枠線・最後のタブの✕ボタン）を通知し直す。</summary>
    internal void NotifySplitStateChanged()
    {
        OnPropertyChanged(nameof(ShowsActiveHighlight));
        OnPropertyChanged(nameof(ShowsTabCloseButton));
    }

    /// <summary>タブをこのペインから外す（別のペインへ移すため。閉じたタブとしては記録しない）。</summary>
    internal void ReleaseTab(BrowserTabViewModel tab)
    {
        var index = Tabs.IndexOf(tab);
        if (index < 0)
        {
            return;
        }

        Tabs.RemoveAt(index);
        tab.IsActive = false;

        if (ReferenceEquals(tab, ActiveTab) && Tabs.Count > 0)
        {
            ActivateTab(Tabs[Math.Min(index, Tabs.Count - 1)]);
        }
    }

    /// <summary>別のペインから移されたタブを受け取る（表示するかどうかは呼び出し側が決める）。</summary>
    internal void AdoptTab(BrowserTabViewModel tab, int index)
    {
        Tabs.Insert(Math.Clamp(index, 0, Tabs.Count), tab);
    }

    /// <summary>タブを閉じられるか（最後の1つは閉じられない）。</summary>
    public bool CanCloseTabs => Tabs.Count > 1;

    /// <summary>タブを増やせるか（上限に達していないか）。</summary>
    public bool CanAddTab => Tabs.Count < MaxTabCount;

    /// <summary>開き直せる「閉じたタブ」があるか。</summary>
    public bool CanReopenClosedTab => _closedTabs.Count > 0;

    /// <summary>
    /// 新しいタブを開いて表示する。パス省略時は現在のタブと同じ場所を開く。
    /// 上限に達している場合は何もせず null を返す。
    /// </summary>
    public BrowserTabViewModel? OpenTab(string? path = null)
    {
        if (!CanAddTab)
        {
            return null;
        }

        var tab = new BrowserTabViewModel(_shell);
        // 新しいタブは、いま見ているタブの表示モードを引き継ぐ
        tab.InitializeFlatFileViewEnabled(ActiveTab.IsFlatFileViewEnabled);
        Tabs.Add(tab);

        // 移動は履歴に積まない（開いた直後のタブに「戻る」先は無いため）
        tab.NavigateTo(path ?? ActiveTab.CurrentPath, false);
        ActivateTab(tab);
        return tab;
    }

    /// <summary>指定タブと同じ場所を開く新しいタブを、そのタブの隣に追加する。</summary>
    public BrowserTabViewModel? DuplicateTab(BrowserTabViewModel tab)
    {
        var duplicated = OpenTab(tab.CurrentPath);
        if (duplicated is null)
        {
            return null;
        }

        var sourceIndex = Tabs.IndexOf(tab);
        if (sourceIndex >= 0 && sourceIndex + 1 < Tabs.Count)
        {
            Tabs.Move(Tabs.IndexOf(duplicated), sourceIndex + 1);
        }

        return duplicated;
    }

    /// <summary>タブを閉じる。最後の1つは閉じない。</summary>
    public bool CloseTab(BrowserTabViewModel tab)
    {
        var index = Tabs.IndexOf(tab);
        if (index < 0 || !CanCloseTabs)
        {
            return false;
        }

        PushClosedTab(tab, index);
        Tabs.RemoveAt(index);

        if (ReferenceEquals(tab, ActiveTab))
        {
            // 閉じた位置にずれ込んだタブ（末尾を閉じた場合は左隣）を表示する
            ActivateTab(Tabs[Math.Min(index, Tabs.Count - 1)]);
        }

        return true;
    }

    /// <summary>表示中のタブを閉じる。</summary>
    public bool CloseActiveTab() => CloseTab(ActiveTab);

    /// <summary>指定タブ以外をすべて閉じ、そのタブを表示する。</summary>
    public void CloseOtherTabs(BrowserTabViewModel tab)
    {
        if (!Tabs.Contains(tab))
        {
            return;
        }

        for (var index = Tabs.Count - 1; index >= 0; index--)
        {
            if (ReferenceEquals(Tabs[index], tab))
            {
                continue;
            }

            PushClosedTab(Tabs[index], index);
            Tabs.RemoveAt(index);
        }

        ActivateTab(tab);
    }

    /// <summary>指定タブを表示する。</summary>
    public void ActivateTab(BrowserTabViewModel tab)
    {
        if (!Tabs.Contains(tab) || ReferenceEquals(tab, ActiveTab))
        {
            return;
        }

        var previous = ActiveTab;
        previous.IsActive = false;

        ActiveTab = tab;
        tab.IsActive = true;

        // 一覧を持ったまま並べておくとタブ数ぶんメモリを食うため、大きい一覧は手放して読み直す
        previous.SuspendIfHeavy();
        tab.OnActivated();
        _shell.OnPaneStateChanged();
    }

    /// <summary>
    /// 保存済みのタブ構成を復元する。1つ目のタブは既にあるものを使い回す。
    /// 移動できなかった（フォルダが無くなった）タブは、最初のルートへ寄せるか取り除く。
    /// </summary>
    internal void RestoreTabs(IReadOnlyList<TabStateSettings> states, int activeTabIndex, string? fallbackPath)
    {
        for (var index = 0; index < states.Count && index < MaxTabCount; index++)
        {
            var state = states[index];
            var tab = index == 0 ? Tabs[0] : new BrowserTabViewModel(_shell);
            if (index > 0)
            {
                Tabs.Add(tab);
            }

            // セッターだと保存や再取得が走るため、初期値として直接入れる
            tab.InitializeFlatFileViewEnabled(state.IsFlatFileViewEnabled);

            if (!tab.NavigateTo(state.Path, false) && fallbackPath is not null)
            {
                tab.NavigateTo(fallbackPath, false);
            }
        }

        // 移動先が1つも見つからなかったタブは残さない（空のタブが並ぶのを防ぐ）。最後の1つは残す
        foreach (var emptyTab in Tabs.Where(tab => string.IsNullOrWhiteSpace(tab.CurrentPath)).ToList())
        {
            if (!CanCloseTabs)
            {
                break;
            }

            Tabs.Remove(emptyTab);
        }

        ActivateTab(Tabs[Math.Clamp(activeTabIndex, 0, Tabs.Count - 1)]);
    }

    /// <summary>settings.json へ保存するための、このペインのタブ構成を組み立てる。</summary>
    internal PaneStateSettings CreateStateSettings()
    {
        return new PaneStateSettings
        {
            Tabs = Tabs
                .Select(tab => new TabStateSettings
                {
                    Path = tab.CurrentPath,
                    IsFlatFileViewEnabled = tab.IsFlatFileViewEnabled
                })
                .ToList(),
            ActiveTabIndex = Math.Max(0, Tabs.IndexOf(ActiveTab)),
            IsTreeVisible = _isTreeVisible
        };
    }

    /// <summary>左から数えて指定位置のタブを表示する（範囲外なら何もしない）。</summary>
    public void ActivateTabAt(int index)
    {
        if (index >= 0 && index < Tabs.Count)
        {
            ActivateTab(Tabs[index]);
        }
    }

    /// <summary>末尾のタブを表示する（Ctrl+9 用）。</summary>
    public void ActivateLastTab() => ActivateTabAt(Tabs.Count - 1);

    /// <summary>隣のタブを表示する。端まで来たら反対の端へ回り込む。</summary>
    public void ActivateAdjacentTab(bool forward)
    {
        if (Tabs.Count <= 1)
        {
            return;
        }

        var index = Tabs.IndexOf(ActiveTab);
        var next = forward
            ? (index + 1) % Tabs.Count
            : (index - 1 + Tabs.Count) % Tabs.Count;

        ActivateTab(Tabs[next]);
    }

    /// <summary>タブを並べ替える（ドラッグでの入れ替え）。</summary>
    public void MoveTab(BrowserTabViewModel tab, int newIndex)
    {
        var oldIndex = Tabs.IndexOf(tab);
        if (oldIndex < 0)
        {
            return;
        }

        newIndex = Math.Clamp(newIndex, 0, Tabs.Count - 1);
        if (oldIndex != newIndex)
        {
            Tabs.Move(oldIndex, newIndex);
        }
    }

    /// <summary>直前に閉じたタブを、閉じた位置に開き直す（Ctrl+Shift+T）。</summary>
    public BrowserTabViewModel? ReopenClosedTab()
    {
        if (_closedTabs.Count == 0 || !CanAddTab)
        {
            // 上限に達している間はスタックから取り出さない（後で開き直せるよう残す）
            return null;
        }

        var state = _closedTabs.Pop();
        OnPropertyChanged(nameof(CanReopenClosedTab));

        var tab = new BrowserTabViewModel(_shell);
        Tabs.Insert(Math.Clamp(state.Index, 0, Tabs.Count), tab);
        tab.RestoreClosedState(state);
        ActivateTab(tab);
        return tab;
    }

    private void PushClosedTab(BrowserTabViewModel tab, int index)
    {
        tab.IsActive = false;
        _closedTabs.Push(tab.CreateClosedState(index));

        // Stack には上限が無いため、溢れた分は古い順に捨てる
        while (_closedTabs.Count > MaxClosedTabHistory)
        {
            var kept = _closedTabs.Take(MaxClosedTabHistory).Reverse().ToList();
            _closedTabs.Clear();
            foreach (var state in kept)
            {
                _closedTabs.Push(state);
            }
        }

        OnPropertyChanged(nameof(CanReopenClosedTab));
    }

    private void NotifyTabCountChanged()
    {
        OnPropertyChanged(nameof(CanCloseTabs));
        OnPropertyChanged(nameof(ShowsTabCloseButton));
        OnPropertyChanged(nameof(CanAddTab));
    }
}
