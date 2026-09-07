using ParallelScope.Data;
using ParallelScope.Tests.TestSupport;
using ParallelScope.ViewModels;

namespace ParallelScope.Tests.ViewModels;

/// <summary>
/// 1ペイン内のタブ操作（追加・複製・クローズ・切り替え・並べ替え・開き直し）の確認。
/// ファイルシステムへ実際に移動できる必要があるため、起点には一時フォルダを使う。
/// </summary>
[Collection(FolderTreeCollection.Name)]
public class BrowserTabTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly TempDirectory _rootA = new();
    private readonly TempDirectory _rootB = new();
    private readonly FileCacheRepository _fileCacheRepository;
    private readonly AppSettingsRepository _settingsRepository;

    public BrowserTabTests()
    {
        _fileCacheRepository = new FileCacheRepository(_temp.Path);
        _settingsRepository = new AppSettingsRepository(_temp.Path);
        _settingsRepository.Save(new AppSettings { RootPaths = { _rootA.Path, _rootB.Path } });
    }

    public void Dispose()
    {
        _fileCacheRepository.ReleasePooledConnections();
        _temp.Dispose();
        _rootA.Dispose();
        _rootB.Dispose();
    }

    private BrowserPaneViewModel CreatePane()
    {
        return new MainWindowViewModel(_fileCacheRepository, _settingsRepository).ActivePane;
    }

    /// <summary>
    /// 非表示のタブが一覧を手放す基準は、タブ数が増えるほど絞られることの確認
    /// （上限を一律にすると、開いているタブ数ぶんだけ一覧が積み上がるため）。
    /// </summary>
    [Fact]
    public void RetainedItemThreshold_ShrinksAsMoreTabsAreOpened()
    {
        var pane = CreatePane();

        // タブが数本のうちは従来どおり手放さない（切り替えのたびに空表示を挟まないため）
        Assert.Equal(5_000, pane.ActiveTab.GetSuspendItemCountThreshold());

        while (pane.CanAddTab)
        {
            pane.OpenTab(_rootB.Path);
        }

        // 20タブでは 20,000 / 20 = 1,000 件まで
        Assert.Equal(BrowserPaneViewModel.MaxTabCount, pane.Tabs.Count);
        Assert.Equal(1_000, pane.ActiveTab.GetSuspendItemCountThreshold());
    }

    [Fact]
    public void NewPane_HasSingleActiveTab()
    {
        var pane = CreatePane();

        Assert.Single(pane.Tabs);
        Assert.Same(pane.Tabs[0], pane.ActiveTab);
        Assert.True(pane.ActiveTab.IsActive);
        Assert.False(pane.CanCloseTabs);
    }

    [Fact]
    public void OpenTab_AddsTabAtCurrentPathAndActivatesIt()
    {
        var pane = CreatePane();
        var first = pane.ActiveTab;

        var opened = pane.OpenTab();

        Assert.NotNull(opened);
        Assert.Equal(2, pane.Tabs.Count);
        Assert.Same(opened, pane.ActiveTab);
        Assert.True(opened!.IsActive);
        Assert.False(first.IsActive);
        Assert.Equal(first.CurrentPath, opened.CurrentPath);
        Assert.True(pane.CanCloseTabs);
    }

    [Fact]
    public void OpenTab_WithPath_OpensThatFolder()
    {
        var pane = CreatePane();

        var opened = pane.OpenTab(_rootB.Path);

        Assert.NotNull(opened);
        Assert.Equal(_rootB.Path, opened!.CurrentPath);
    }

    [Fact]
    public void OpenTab_StopsAtTabLimit()
    {
        var pane = CreatePane();

        while (pane.CanAddTab)
        {
            Assert.NotNull(pane.OpenTab());
        }

        Assert.Equal(BrowserPaneViewModel.MaxTabCount, pane.Tabs.Count);
        Assert.Null(pane.OpenTab());
        Assert.Equal(BrowserPaneViewModel.MaxTabCount, pane.Tabs.Count);
    }

    [Fact]
    public void DuplicateTab_InsertsNextToTheSourceTab()
    {
        var pane = CreatePane();
        var first = pane.ActiveTab;
        pane.OpenTab(_rootB.Path);

        var duplicated = pane.DuplicateTab(first);

        Assert.NotNull(duplicated);
        Assert.Equal(1, pane.Tabs.IndexOf(duplicated!));
        Assert.Equal(first.CurrentPath, duplicated!.CurrentPath);
    }

    [Fact]
    public void CloseTab_LastTabIsKept()
    {
        var pane = CreatePane();

        Assert.False(pane.CloseTab(pane.ActiveTab));
        Assert.Single(pane.Tabs);
    }

    [Fact]
    public void CloseTab_ActivatesTheTabThatTakesItsPlace()
    {
        var pane = CreatePane();
        var first = pane.ActiveTab;
        var second = pane.OpenTab(_rootB.Path)!;
        var third = pane.OpenTab(_rootA.Path)!;
        pane.ActivateTab(second);

        Assert.True(pane.CloseTab(second));

        Assert.Equal(new[] { first, third }, pane.Tabs);
        Assert.Same(third, pane.ActiveTab);
    }

    [Fact]
    public void CloseTab_LastPositionFallsBackToTheLeftNeighbor()
    {
        var pane = CreatePane();
        var first = pane.ActiveTab;
        var second = pane.OpenTab(_rootB.Path)!;

        Assert.True(pane.CloseTab(second));

        Assert.Same(first, pane.ActiveTab);
    }

    [Fact]
    public void CloseOtherTabs_KeepsOnlyTheGivenTab()
    {
        var pane = CreatePane();
        pane.OpenTab(_rootB.Path);
        var kept = pane.OpenTab(_rootA.Path)!;
        pane.OpenTab(_rootB.Path);

        pane.CloseOtherTabs(kept);

        Assert.Single(pane.Tabs);
        Assert.Same(kept, pane.ActiveTab);
    }

    [Fact]
    public void ActivateAdjacentTab_WrapsAroundBothWays()
    {
        var pane = CreatePane();
        var first = pane.ActiveTab;
        var second = pane.OpenTab(_rootB.Path)!;
        pane.ActivateTab(first);

        pane.ActivateAdjacentTab(forward: false);
        Assert.Same(second, pane.ActiveTab);

        pane.ActivateAdjacentTab(forward: true);
        Assert.Same(first, pane.ActiveTab);
    }

    [Fact]
    public void ActivateTabAt_IgnoresOutOfRangeIndex()
    {
        var pane = CreatePane();
        var first = pane.ActiveTab;

        pane.ActivateTabAt(5);

        Assert.Same(first, pane.ActiveTab);
    }

    [Fact]
    public void ActivateLastTab_SelectsTheRightmostTab()
    {
        var pane = CreatePane();
        pane.OpenTab(_rootB.Path);
        var last = pane.OpenTab(_rootA.Path)!;
        pane.ActivateTabAt(0);

        pane.ActivateLastTab();

        Assert.Same(last, pane.ActiveTab);
    }

    [Fact]
    public void MoveTab_ReordersTabs()
    {
        var pane = CreatePane();
        var first = pane.ActiveTab;
        var second = pane.OpenTab(_rootB.Path)!;

        pane.MoveTab(second, 0);

        Assert.Equal(new[] { second, first }, pane.Tabs);
    }

    [Fact]
    public void ReopenClosedTab_RestoresPathAndPosition()
    {
        var pane = CreatePane();
        pane.OpenTab(_rootB.Path);
        var closed = pane.OpenTab(_rootA.Path)!;
        pane.MoveTab(closed, 1);
        var closedPath = closed.CurrentPath;

        Assert.True(pane.CloseTab(closed));
        Assert.True(pane.CanReopenClosedTab);

        var reopened = pane.ReopenClosedTab();

        Assert.NotNull(reopened);
        Assert.Equal(closedPath, reopened!.CurrentPath);
        Assert.Equal(1, pane.Tabs.IndexOf(reopened));
        Assert.Same(reopened, pane.ActiveTab);
        Assert.False(pane.CanReopenClosedTab);
    }

    [Fact]
    public void ReopenClosedTab_RestoresBackHistory()
    {
        var pane = CreatePane();
        var tab = pane.OpenTab(_rootA.Path)!;
        tab.NavigateTo(_rootB.Path, true);

        Assert.True(tab.CanGoBack);
        Assert.True(pane.CloseTab(tab));

        var reopened = pane.ReopenClosedTab();

        Assert.NotNull(reopened);
        Assert.True(reopened!.CanGoBack);
        Assert.True(reopened.GoBack());
        Assert.Equal(_rootA.Path, reopened.CurrentPath);
    }

    [Fact]
    public void ReopenClosedTab_DoesNothingWhenNoTabWasClosed()
    {
        var pane = CreatePane();

        Assert.False(pane.CanReopenClosedTab);
        Assert.Null(pane.ReopenClosedTab());
    }

    [Fact]
    public void ReopenClosedTab_KeepsHistoryWhileAtTheTabLimit()
    {
        var pane = CreatePane();
        var closed = pane.OpenTab(_rootB.Path)!;
        Assert.True(pane.CloseTab(closed));

        while (pane.CanAddTab)
        {
            pane.OpenTab();
        }

        // 上限に達している間は開き直せないが、控えは残しておく
        Assert.Null(pane.ReopenClosedTab());
        Assert.True(pane.CanReopenClosedTab);
    }

    [Fact]
    public void ClosedTabHistory_KeepsOnlyTheMostRecentTen()
    {
        var pane = CreatePane();
        var opened = new List<BrowserTabViewModel>();
        for (var i = 0; i < 12; i++)
        {
            opened.Add(pane.OpenTab(i % 2 == 0 ? _rootA.Path : _rootB.Path)!);
        }

        foreach (var tab in opened)
        {
            Assert.True(pane.CloseTab(tab));
        }

        var reopenedCount = 0;
        while (pane.ReopenClosedTab() is not null)
        {
            reopenedCount++;
        }

        Assert.Equal(10, reopenedCount);
    }

    [Fact]
    public void ActiveTabChange_IsNotifiedToTheShell()
    {
        var shell = new MainWindowViewModel(_fileCacheRepository, _settingsRepository);
        var pane = shell.ActivePane;
        var changed = new List<string?>();
        shell.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        var opened = pane.OpenTab(_rootB.Path)!;

        Assert.Same(opened, shell.ActiveTab);
        Assert.Equal(_rootB.Path, shell.CurrentPath);
        Assert.Contains(nameof(MainWindowViewModel.CurrentPath), changed);
        Assert.Contains(nameof(MainWindowViewModel.FileItems), changed);
    }

    [Theory]
    [InlineData(@"C:\", @"C:\")]
    [InlineData(@"C:\Users\Sample", "Sample")]
    [InlineData(@"C:\Users\Sample\", "Sample")]
    public void DisplayName_UsesFolderNameAndFallsBackToThePath(string path, string expected)
    {
        var pane = CreatePane();
        var tab = pane.ActiveTab;

        // 見出しは現在パスからそのまま組み立てるので、移動を伴わずに設定して確認する
        tab.CurrentPath = path;

        Assert.Equal(expected, tab.DisplayName);
    }
}
