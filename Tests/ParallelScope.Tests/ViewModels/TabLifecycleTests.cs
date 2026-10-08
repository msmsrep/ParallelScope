using ParallelScope.Data;
using ParallelScope.Tests.TestSupport;
using ParallelScope.Utilities;
using ParallelScope.ViewModels;

namespace ParallelScope.Tests.ViewModels;

/// <summary>
/// 表示していないタブの一覧の扱い（大きい一覧は手放す・スキャン後は次の表示で読み直す）と、
/// 表示中のフォルダの読み直しの確認。一覧はライブのファイルシステムからも読み直されるため、中身は実在のファイルで用意する。
/// </summary>
[Collection(SharedStateCollection.Name)]
public class TabLifecycleTests : ShellTestBase
{
    private readonly string _root;
    private readonly string _small;

    public TabLifecycleTests()
    {
        _root = PathNormalizer.Normalize(NewTempDirectory().Path);
        _small = Directory.CreateDirectory(Path.Combine(_root, "Small")).FullName;
        File.WriteAllText(Path.Combine(_small, "a.txt"), "x");
        SettingsRepository.Save(new AppSettings { RootPaths = { _root } });
    }

    private string CreateFolderWithFiles(string name, int fileCount)
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root, name)).FullName;
        for (var i = 0; i < fileCount; i++)
        {
            File.WriteAllBytes(Path.Combine(folder, $"file{i:D6}.txt"), Array.Empty<byte>());
        }

        return folder;
    }

    /// <summary>ペインの上限までタブを開いて、1タブあたりの基準件数を最小（1,000件）まで絞った状態を作る。</summary>
    private static BrowserPaneViewModel FillPaneWithTabs(MainWindowViewModel viewModel)
    {
        var pane = viewModel.ActivePane;
        while (pane.Tabs.Count < BrowserPaneViewModel.MaxTabCount)
        {
            pane.OpenTab();
        }

        pane.ActivateTabAt(0);
        return pane;
    }

    [Fact]
    public void RefreshCurrentFolder_PicksUpFilesAddedOnDisk()
    {
        var tab = CreateViewModel().ActiveTab;
        tab.LoadFiles(_small);
        Wait.ForItemNames(tab, "a.txt");

        File.WriteAllText(Path.Combine(_small, "b.txt"), "x");
        tab.RefreshCurrentFolder();

        Wait.ForItemNames(tab, "a.txt", "b.txt");
    }

    [Fact]
    public void HiddenTabWithALargeList_ReleasesItAndReloadsWhenShownAgain()
    {
        var large = CreateFolderWithFiles("Large", 1_000);
        var pane = FillPaneWithTabs(CreateViewModel());
        var tab = pane.ActiveTab;
        Assert.Equal(1_000, tab.GetSuspendItemCountThreshold());
        tab.LoadFiles(large);
        Wait.ForItemCount(tab, 1_000);

        pane.ActivateTabAt(1);

        Assert.Empty(tab.FileItems);

        pane.ActivateTabAt(0);

        Wait.ForItemCount(tab, 1_000);
    }

    // 小さい一覧は手放さない（切り替えのたびに空表示を挟まないため）
    [Fact]
    public void HiddenTabWithASmallList_KeepsIt()
    {
        var pane = FillPaneWithTabs(CreateViewModel());
        var tab = pane.ActiveTab;
        tab.LoadFiles(_small);
        Wait.ForItemNames(tab, "a.txt");

        pane.ActivateTabAt(1);

        Assert.Single(tab.FileItems);
    }

    // 表示中のタブはその場で読み直し、表示していないタブは次に表示したときに読み直す
    [Fact]
    public void RefreshAfterScan_ReloadsVisibleTabsNowAndHiddenTabsWhenShown()
    {
        var other = Directory.CreateDirectory(Path.Combine(_root, "Other")).FullName;
        var viewModel = CreateViewModel();
        var pane = viewModel.ActivePane;
        var hiddenTab = pane.ActiveTab;
        hiddenTab.LoadFiles(_small);
        Wait.ForItemNames(hiddenTab, "a.txt");
        var visibleTab = pane.OpenTab(other)!;
        Wait.ForItemCount(visibleTab, 0);

        File.WriteAllText(Path.Combine(_small, "b.txt"), "x");
        File.WriteAllText(Path.Combine(other, "c.txt"), "x");
        viewModel.RefreshAfterScan();

        Wait.ForItemNames(visibleTab, "c.txt");

        pane.ActivateTab(hiddenTab);

        Wait.ForItemNames(hiddenTab, "a.txt", "b.txt");
    }

    // 印の付いていない（スキャンを挟んでいない）タブは、表示し直しても読み直さない
    [Fact]
    public void ShowingAnUnchangedHiddenTab_DoesNotReload()
    {
        var viewModel = CreateViewModel();
        var pane = viewModel.ActivePane;
        var tab = pane.ActiveTab;
        tab.LoadFiles(_small);
        Wait.ForItemNames(tab, "a.txt");
        var items = tab.FileItems;
        pane.OpenTab();

        pane.ActivateTab(tab);

        Assert.Same(items, tab.FileItems);
    }

    // 一覧はまずキャッシュから出し、裏でファイルシステムを読み直して一覧とキャッシュを置き換える
    [Fact]
    public void OpeningAFolder_ReplacesTheCachedListWithTheLiveOne()
    {
        FileCacheRepository.ReplaceEntriesByParentPath(_small, new[] { CacheEntries.File(_small, "stale.txt") });
        var tab = CreateViewModel().ActiveTab;

        tab.LoadFiles(_small);

        Wait.ForItemNames(tab, "a.txt");
        Wait.Until(
            () => FileCacheRepository.GetEntriesByParentPath(_small).Select(x => x.Name).SequenceEqual(new[] { "a.txt" }),
            () => "キャッシュがファイルシステムの内容へ置き換わりませんでした");
    }
}
