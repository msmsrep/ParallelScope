using ParallelScope.Data;
using ParallelScope.Tests.TestSupport;
using ParallelScope.ViewModels;

namespace ParallelScope.Tests.ViewModels;

/// <summary>2画面（分割表示）でのペインの増減・操作対象の切り替え・タブのペイン間移動の確認。</summary>
[Collection(FolderTreeCollection.Name)]
public class SplitViewTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly TempDirectory _rootA = new();
    private readonly TempDirectory _rootB = new();
    private readonly FileCacheRepository _fileCacheRepository;
    private readonly AppSettingsRepository _settingsRepository;

    public SplitViewTests()
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

    private MainWindowViewModel CreateViewModel()
    {
        return new MainWindowViewModel(_fileCacheRepository, _settingsRepository);
    }

    [Fact]
    public void NewViewModel_HasSingleActivePane()
    {
        var viewModel = CreateViewModel();

        Assert.Single(viewModel.Panes);
        Assert.False(viewModel.IsSplitViewEnabled);
        Assert.True(viewModel.ActivePane.IsActive);
        // 1画面のときはアクティブペインの枠線を出さない
        Assert.False(viewModel.ActivePane.ShowsActiveHighlight);
    }

    [Fact]
    public void EnableSplitView_AddsSecondPaneAtTheSameFolder()
    {
        var viewModel = CreateViewModel();
        var firstPane = viewModel.ActivePane;

        var secondPane = viewModel.EnableSplitView();

        Assert.Equal(2, viewModel.Panes.Count);
        Assert.True(viewModel.IsSplitViewEnabled);
        Assert.Equal(firstPane.ActiveTab.CurrentPath, secondPane.ActiveTab.CurrentPath);
        // 追加しただけでは操作対象は変わらない
        Assert.Same(firstPane, viewModel.ActivePane);
        Assert.True(firstPane.ShowsActiveHighlight);
        Assert.False(secondPane.ShowsActiveHighlight);
    }

    [Fact]
    public void EnableSplitView_GivesEachPaneItsOwnTree()
    {
        var viewModel = CreateViewModel();
        var firstPane = viewModel.ActivePane;

        var secondPane = viewModel.EnableSplitView();

        Assert.NotSame(firstPane.TreeRoots, secondPane.TreeRoots);
        Assert.NotSame(firstPane.AllRootsNode, secondPane.AllRootsNode);
        Assert.Equal(
            firstPane.RootFolders.Select(x => x.Path),
            secondPane.RootFolders.Select(x => x.Path));
    }

    [Fact]
    public void SetActivePane_SwitchesWhatTheShellShows()
    {
        var viewModel = CreateViewModel();
        var secondPane = viewModel.EnableSplitView();
        secondPane.ActiveTab.NavigateTo(_rootB.Path, false);

        viewModel.SetActivePane(secondPane);

        Assert.Same(secondPane, viewModel.ActivePane);
        Assert.Same(secondPane.ActiveTab, viewModel.ActiveTab);
        Assert.Equal(_rootB.Path, viewModel.CurrentPath);
        Assert.Same(secondPane.TreeRoots, viewModel.TreeRoots);
        Assert.False(viewModel.Panes[0].IsActive);
    }

    [Fact]
    public void GetOtherPane_ReturnsNullWhileNotSplit()
    {
        var viewModel = CreateViewModel();

        Assert.Null(viewModel.GetOtherPane(viewModel.ActivePane));

        var secondPane = viewModel.EnableSplitView();

        Assert.Same(secondPane, viewModel.GetOtherPane(viewModel.Panes[0]));
        Assert.Same(viewModel.Panes[0], viewModel.GetOtherPane(secondPane));
    }

    [Fact]
    public void DisableSplitView_MovesTabsIntoTheRemainingPane()
    {
        var viewModel = CreateViewModel();
        var firstPane = viewModel.ActivePane;
        var secondPane = viewModel.EnableSplitView();
        var movedTab = secondPane.OpenTab(_rootB.Path)!;

        viewModel.DisableSplitView();

        Assert.Single(viewModel.Panes);
        Assert.False(viewModel.IsSplitViewEnabled);
        Assert.Same(firstPane, viewModel.ActivePane);
        // 2つ目のペインのタブ（元から1つ＋追加した1つ）が末尾へ移っている
        Assert.Equal(3, firstPane.Tabs.Count);
        Assert.Contains(movedTab, firstPane.Tabs);
    }

    [Fact]
    public void DisableSplitView_DropsTabsThatDoNotFit()
    {
        var viewModel = CreateViewModel();
        var firstPane = viewModel.ActivePane;
        var secondPane = viewModel.EnableSplitView();

        while (firstPane.CanAddTab)
        {
            firstPane.OpenTab(_rootA.Path);
        }

        secondPane.OpenTab(_rootB.Path);

        viewModel.DisableSplitView();

        Assert.Equal(BrowserPaneViewModel.MaxTabCount, firstPane.Tabs.Count);
    }

    [Fact]
    public void MoveTabToPane_MovesTheInstanceWithItsState()
    {
        var viewModel = CreateViewModel();
        var firstPane = viewModel.ActivePane;
        var secondPane = viewModel.EnableSplitView();
        var moved = firstPane.OpenTab(_rootB.Path)!;
        moved.IsFlatFileViewEnabled = true;

        Assert.True(viewModel.MoveTabToPane(moved, secondPane, 0));

        Assert.DoesNotContain(moved, firstPane.Tabs);
        Assert.Equal(0, secondPane.Tabs.IndexOf(moved));
        Assert.Same(moved, secondPane.ActiveTab);
        // 移動先が操作対象になる
        Assert.Same(secondPane, viewModel.ActivePane);
        Assert.True(moved.IsFlatFileViewEnabled);
        Assert.Equal(_rootB.Path, moved.CurrentPath);
    }

    [Fact]
    public void MoveTabToPane_KeepsTheLastTabInTheSourcePane()
    {
        var viewModel = CreateViewModel();
        var firstPane = viewModel.ActivePane;
        var secondPane = viewModel.EnableSplitView();
        var onlyTab = firstPane.ActiveTab;

        Assert.False(viewModel.MoveTabToPane(onlyTab, secondPane, 0));

        Assert.Single(firstPane.Tabs);
        Assert.Contains(onlyTab, firstPane.Tabs);
    }

    [Fact]
    public void MoveTabToPane_RefusesWhenTheTargetIsFull()
    {
        var viewModel = CreateViewModel();
        var firstPane = viewModel.ActivePane;
        var secondPane = viewModel.EnableSplitView();
        var moved = firstPane.OpenTab(_rootB.Path)!;

        while (secondPane.CanAddTab)
        {
            secondPane.OpenTab(_rootA.Path);
        }

        Assert.False(viewModel.MoveTabToPane(moved, secondPane, 0));
        Assert.Contains(moved, firstPane.Tabs);
    }

    [Fact]
    public void FindPaneOf_ReturnsThePaneHoldingTheTab()
    {
        var viewModel = CreateViewModel();
        var secondPane = viewModel.EnableSplitView();

        Assert.Same(viewModel.Panes[0], viewModel.FindPaneOf(viewModel.Panes[0].ActiveTab));
        Assert.Same(secondPane, viewModel.FindPaneOf(secondPane.ActiveTab));
    }

    [Fact]
    public void SetSplitRatio_IsClampedToAUsableRange()
    {
        var viewModel = CreateViewModel();

        viewModel.SetSplitRatio(0.01);
        Assert.Equal(0.1, viewModel.SplitRatio, 3);

        viewModel.SetSplitRatio(0.99);
        Assert.Equal(0.9, viewModel.SplitRatio, 3);

        viewModel.SetSplitRatio(double.NaN);
        Assert.Equal(0.9, viewModel.SplitRatio, 3);
    }

    [Fact]
    public void SetSplitOrientation_KeepsTheChosenDirection()
    {
        var viewModel = CreateViewModel();

        Assert.Equal(PaneSplitOrientation.Vertical, viewModel.SplitOrientation);

        viewModel.SetSplitOrientation(PaneSplitOrientation.Horizontal);

        Assert.Equal(PaneSplitOrientation.Horizontal, viewModel.SplitOrientation);
    }

    [Fact]
    public void RootPathChange_IsAppliedToEveryPaneTree()
    {
        var viewModel = CreateViewModel();
        var secondPane = viewModel.EnableSplitView();

        viewModel.ApplyRootPaths(new[] { _rootB.Path });

        Assert.Equal(new[] { _rootB.Path }, viewModel.Panes[0].RootFolders.Select(x => x.Path));
        Assert.Equal(new[] { _rootB.Path }, secondPane.RootFolders.Select(x => x.Path));
    }
}
