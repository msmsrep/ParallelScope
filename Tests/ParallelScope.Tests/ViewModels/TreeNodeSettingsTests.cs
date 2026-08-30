using ParallelScope.Data;
using ParallelScope.Tests.TestSupport;
using ParallelScope.Utilities;
using ParallelScope.ViewModels;

namespace ParallelScope.Tests.ViewModels;

/// <summary>ツリー最上位ノードの表示/非表示・並び順の設定が反映され、保存・復元されることの確認。</summary>
public class TreeNodeSettingsTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly FileCacheRepository _fileCacheRepository;
    private readonly AppSettingsRepository _settingsRepository;

    public TreeNodeSettingsTests()
    {
        _fileCacheRepository = new FileCacheRepository(_temp.Path);
        _settingsRepository = new AppSettingsRepository(_temp.Path);
        _settingsRepository.Save(new AppSettings { RootPaths = { @"C:\TreeNodeTest" } });
    }

    public void Dispose()
    {
        _fileCacheRepository.ReleasePooledConnections();
        _temp.Dispose();
    }

    private MainWindowViewModel CreateViewModel()
    {
        return new MainWindowViewModel(_fileCacheRepository, _settingsRepository);
    }

    private static IReadOnlyList<string> TreeRootPaths(MainWindowViewModel viewModel)
    {
        return viewModel.TreeRoots.Select(x => x.Path).ToList();
    }

    private static void ApplyTreeNodes(
        MainWindowViewModel viewModel,
        IEnumerable<string> visibleTreeNodes,
        IEnumerable<string> treeNodeOrder)
    {
        viewModel.ApplySettings(
            viewModel.GetConfiguredRootPaths(),
            viewModel.GetExcludedPaths(),
            viewModel.GetFullScanIntervalHours(),
            viewModel.GetVisibleColumns(),
            viewModel.GetColumnOrder(),
            visibleTreeNodes,
            treeNodeOrder);
    }

    [Fact]
    public void Defaults_ShowEveryNodeInTheDefaultOrder()
    {
        var viewModel = CreateViewModel();
        viewModel.SetPlusFeaturesEnabled(true);

        Assert.Equal(TreeNodes.AllNodes, viewModel.GetTreeNodeOrder());
        Assert.Equal(TreeNodes.DefaultVisibleNodes, viewModel.GetVisibleTreeNodes());
        Assert.Equal(
            new[] { VirtualFolders.AllRootsPath, VirtualFolders.FavoritesPath, VirtualFolders.RecentPath, VirtualFolders.FrequentPath },
            TreeRootPaths(viewModel));
    }

    [Fact]
    public void HidingNodes_RemovesThemFromTheTree()
    {
        var viewModel = CreateViewModel();
        viewModel.SetPlusFeaturesEnabled(true);

        ApplyTreeNodes(viewModel, new[] { TreeNodes.Favorites }, TreeNodes.AllNodes);

        Assert.Equal(
            new[] { VirtualFolders.AllRootsPath, VirtualFolders.FavoritesPath },
            TreeRootPaths(viewModel));
    }

    [Fact]
    public void HidingEveryOptionalNode_StillLeavesTheFoldersNode()
    {
        var viewModel = CreateViewModel();
        viewModel.SetPlusFeaturesEnabled(true);

        ApplyTreeNodes(viewModel, Array.Empty<string>(), TreeNodes.AllNodes);

        Assert.Equal(new[] { VirtualFolders.AllRootsPath }, TreeRootPaths(viewModel));
    }

    [Fact]
    public void ReorderingNodes_ReordersTheTree()
    {
        var viewModel = CreateViewModel();
        viewModel.SetPlusFeaturesEnabled(true);

        // Foldersを先頭へ、残りを逆順にする
        var order = new[] { TreeNodes.AllRoots, TreeNodes.Frequent, TreeNodes.Recent, TreeNodes.Favorites };
        ApplyTreeNodes(viewModel, TreeNodes.DefaultVisibleNodes, order);

        Assert.Equal(
            new[] { VirtualFolders.AllRootsPath, VirtualFolders.FrequentPath, VirtualFolders.RecentPath, VirtualFolders.FavoritesPath },
            TreeRootPaths(viewModel));
    }

    [Fact]
    public void HidingTheNodeYouAreLookingAt_MovesBackToFolders()
    {
        var viewModel = CreateViewModel();
        viewModel.SetPlusFeaturesEnabled(true);
        viewModel.LoadFiles(VirtualFolders.RecentPath);
        Assert.Equal(VirtualFolders.RecentPath, viewModel.CurrentPath);

        ApplyTreeNodes(viewModel, new[] { TreeNodes.Favorites, TreeNodes.Frequent }, TreeNodes.AllNodes);

        Assert.Equal(VirtualFolders.AllRootsPath, viewModel.CurrentPath);
    }

    [Fact]
    public void Settings_AreSavedAndRestored()
    {
        var order = new[] { TreeNodes.AllRoots, TreeNodes.Recent, TreeNodes.Favorites, TreeNodes.Frequent };
        var viewModel = CreateViewModel();
        ApplyTreeNodes(viewModel, new[] { TreeNodes.Recent }, order);

        var saved = _settingsRepository.Load();
        Assert.Equal(new[] { TreeNodes.Recent }, saved.VisibleTreeNodes);
        Assert.Equal(order, saved.TreeNodeOrder);

        var restarted = CreateViewModel();
        restarted.SetPlusFeaturesEnabled(true);

        Assert.Equal(order, restarted.GetTreeNodeOrder());
        Assert.Equal(new[] { TreeNodes.Recent }, restarted.GetVisibleTreeNodes());
        Assert.Equal(
            new[] { VirtualFolders.AllRootsPath, VirtualFolders.RecentPath },
            TreeRootPaths(restarted));
    }

    [Fact]
    public void OlderSettingsWithoutTheKeys_FallBackToTheDefaults()
    {
        // 旧バージョンが書いた settings.json（ツリーノードの項目が無い）
        _settingsRepository.Save(new AppSettings
        {
            RootPaths = { @"C:\TreeNodeTest" },
            VisibleTreeNodes = null,
            TreeNodeOrder = null
        });

        var viewModel = CreateViewModel();

        Assert.Equal(TreeNodes.AllNodes, viewModel.GetTreeNodeOrder());
        Assert.Equal(TreeNodes.DefaultVisibleNodes, viewModel.GetVisibleTreeNodes());
    }

    [Fact]
    public void PartialOrderFromOlderSettings_IsFilledUpWithTheMissingNodes()
    {
        var viewModel = CreateViewModel();

        // 未知のキーは捨て、指定に無いノードは既定の並び順で末尾に補う
        ApplyTreeNodes(viewModel, TreeNodes.DefaultVisibleNodes, new[] { "RemovedNode", TreeNodes.Frequent });

        Assert.Equal(
            new[] { TreeNodes.Frequent, TreeNodes.AllRoots, TreeNodes.Favorites, TreeNodes.Recent },
            viewModel.GetTreeNodeOrder());
    }

    [Fact]
    public void CustomOrder_IsIgnoredWhileTheFreeVersionHidesThePlusNodes()
    {
        var viewModel = CreateViewModel();
        ApplyTreeNodes(viewModel, TreeNodes.DefaultVisibleNodes, new[] { TreeNodes.AllRoots, TreeNodes.Favorites, TreeNodes.Recent, TreeNodes.Frequent });

        // 未購読の間はFoldersだけ。設定は残っているので購読すれば並び順ごと復活する
        Assert.Equal(new[] { VirtualFolders.AllRootsPath }, TreeRootPaths(viewModel));

        viewModel.SetPlusFeaturesEnabled(true);

        Assert.Equal(
            new[] { VirtualFolders.AllRootsPath, VirtualFolders.FavoritesPath, VirtualFolders.RecentPath, VirtualFolders.FrequentPath },
            TreeRootPaths(viewModel));
    }
}
