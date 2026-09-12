using System.IO;
using ParallelScope.Data;
using ParallelScope.Tests.TestSupport;
using ParallelScope.ViewModels;

namespace ParallelScope.Tests.ViewModels;

/// <summary>
/// タブ構成・分割状態の保存と復元の確認。
/// アプリと同じく「起動 → 購読状態が確定した時点で <see cref="MainWindowViewModel.RestorePanes"/>」の順で操作する。
/// </summary>
[Collection(FolderTreeCollection.Name)]
public class PaneRestoreTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly TempDirectory _rootA = new();
    private readonly TempDirectory _rootB = new();
    private readonly FileCacheRepository _fileCacheRepository;
    private readonly AppSettingsRepository _settingsRepository;

    public PaneRestoreTests()
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

    /// <summary>アプリの起動と同じ手順（生成 → 購読状態が確定して復元）でViewModelを用意する。</summary>
    private MainWindowViewModel Start(bool arePlusFeaturesEnabled = true)
    {
        var viewModel = new MainWindowViewModel(_fileCacheRepository, _settingsRepository);
        viewModel.SetPlusFeaturesEnabled(arePlusFeaturesEnabled);
        viewModel.RestorePanes(arePlusFeaturesEnabled);
        return viewModel;
    }

    [Fact]
    public void Tabs_AreRestoredInTheSameOrder()
    {
        var first = Start();
        var subFolder = Directory.CreateDirectory(Path.Combine(_rootA.Path, "Sub")).FullName;
        first.ActivePane.OpenTab(_rootB.Path);
        first.ActivePane.OpenTab(subFolder);
        first.ActivePane.ActivateTabAt(1);

        var restarted = Start();

        Assert.Equal(3, restarted.ActivePane.Tabs.Count);
        Assert.Equal(
            new[] { _rootA.Path, _rootB.Path, subFolder },
            restarted.ActivePane.Tabs.Select(tab => tab.CurrentPath));
        Assert.Equal(_rootB.Path, restarted.ActiveTab.CurrentPath);
    }

    [Fact]
    public void FlatFileViewMode_IsRestoredPerTab()
    {
        var first = Start();
        var flatTab = first.ActivePane.OpenTab(_rootB.Path)!;
        flatTab.IsFlatFileViewEnabled = true;
        first.ActivePane.ActivateTabAt(0);

        var restarted = Start();

        Assert.False(restarted.ActivePane.Tabs[0].IsFlatFileViewEnabled);
        Assert.True(restarted.ActivePane.Tabs[1].IsFlatFileViewEnabled);
    }

    [Fact]
    public void SplitView_IsRestoredWithBothPanes()
    {
        var first = Start();
        var secondPane = first.EnableSplitView();
        secondPane.ActiveTab.NavigateTo(_rootB.Path, false);
        secondPane.OpenTab(_rootA.Path);
        first.SetActivePane(secondPane);
        first.SetSplitOrientation(PaneSplitOrientation.Horizontal);
        first.SetSplitRatio(0.3);

        var restarted = Start();

        Assert.True(restarted.IsSplitViewEnabled);
        Assert.Equal(2, restarted.Panes.Count);
        Assert.Equal(PaneSplitOrientation.Horizontal, restarted.SplitOrientation);
        Assert.Equal(0.3, restarted.SplitRatio, 3);
        Assert.Equal(2, restarted.Panes[1].Tabs.Count);
        Assert.Same(restarted.Panes[1], restarted.ActivePane);
    }

    [Fact]
    public void MissingFolder_FallsBackToTheFirstRoot()
    {
        var first = Start();
        var removed = Directory.CreateDirectory(Path.Combine(_rootA.Path, "Removed")).FullName;
        first.ActivePane.OpenTab(removed);
        Directory.Delete(removed);

        var restarted = Start();

        Assert.Equal(2, restarted.ActivePane.Tabs.Count);
        Assert.Equal(_rootA.Path, restarted.ActivePane.Tabs[1].CurrentPath);
    }

    /// <summary>
    /// 表示しないタブは復元時に読み込まない（移動先を控えるだけ）ことの確認。
    /// 移動できるかどうかの判定も初回表示まで行わないため、消えたフォルダを開いていたタブは
    /// 表示するまで保存時のパスのままになり、表示した時点でルートへ寄る。
    /// </summary>
    [Fact]
    public void HiddenTab_IsNotLoadedUntilItIsShown()
    {
        var first = Start();
        var removed = Directory.CreateDirectory(Path.Combine(_rootA.Path, "Removed")).FullName;
        first.ActivePane.OpenTab(removed);
        // 消えたフォルダのタブを表示しない状態で保存する
        first.ActivePane.ActivateTabAt(0);
        Directory.Delete(removed);

        var restarted = Start();
        var hiddenTab = restarted.ActivePane.Tabs[1];

        Assert.Equal(removed, hiddenTab.CurrentPath);
        Assert.Empty(hiddenTab.FileItems);

        restarted.ActivePane.ActivateTabAt(1);

        Assert.Equal(_rootA.Path, hiddenTab.CurrentPath);
    }

    /// <summary>表示を遅らせたタブも、閉じて開き直すまでの間に構成が変わればその内容で開く。</summary>
    [Fact]
    public void HiddenTab_FollowsRootChangesMadeBeforeItIsShown()
    {
        var first = Start();
        first.ActivePane.OpenTab(_rootB.Path);
        first.ActivePane.ActivateTabAt(0);

        var restarted = Start();
        // _rootB を対象から外すと、まだ表示していないタブも次に開いた時点で残ったルートへ寄る
        restarted.ApplyRootPaths(new[] { _rootA.Path });
        restarted.ActivePane.ActivateTabAt(1);

        Assert.Equal(_rootA.Path, restarted.ActivePane.Tabs[1].CurrentPath);
    }

    [Fact]
    public void WithoutPlus_OnlyTheFirstTabIsOpenedAndTheSavedLayoutIsKept()
    {
        var first = Start();
        first.ActivePane.OpenTab(_rootB.Path);
        first.EnableSplitView();

        // 未購読で起動: 復元は行わない
        var free = Start(arePlusFeaturesEnabled: false);

        Assert.Single(free.Panes);
        Assert.Single(free.ActivePane.Tabs);
        Assert.False(free.IsSplitViewEnabled);

        // 未購読のまま操作しても、保存済みの構成は消えない
        free.ActivePane.ActiveTab.NavigateTo(_rootB.Path, true);

        var subscribed = Start();

        Assert.True(subscribed.IsSplitViewEnabled);
        Assert.Equal(2, subscribed.Panes[0].Tabs.Count);
    }

    [Fact]
    public void SettingsWithoutPaneState_StartsWithASingleTab()
    {
        // 旧バージョンの settings.json（Panesを持たない）からの移行
        _settingsRepository.Save(new AppSettings
        {
            RootPaths = { _rootA.Path, _rootB.Path },
            IsFlatFileViewEnabled = true
        });

        var viewModel = Start();

        Assert.Single(viewModel.Panes);
        Assert.Single(viewModel.ActivePane.Tabs);
        Assert.False(viewModel.IsSplitViewEnabled);
        // 旧設定のAll Filesモードはそのまま引き継ぐ
        Assert.True(viewModel.ActiveTab.IsFlatFileViewEnabled);
    }

    [Fact]
    public void NewTab_InheritsTheFlatFileViewModeOfTheCurrentTab()
    {
        var viewModel = Start();
        viewModel.ActiveTab.IsFlatFileViewEnabled = true;

        var opened = viewModel.ActivePane.OpenTab(_rootB.Path)!;

        Assert.True(opened.IsFlatFileViewEnabled);
    }

    [Fact]
    public void ClosedTab_IsNotRestoredOnTheNextStart()
    {
        var first = Start();
        var closed = first.ActivePane.OpenTab(_rootB.Path)!;
        first.ActivePane.CloseTab(closed);

        var restarted = Start();

        Assert.Single(restarted.ActivePane.Tabs);
        Assert.Equal(_rootA.Path, restarted.ActiveTab.CurrentPath);
    }
}
