using ParallelScope.Utilities;

namespace ParallelScope.Tests.Utilities;

public class TreeNodesTests
{
    [Fact]
    public void AllNodes_IsAllRootsFollowedByOptionalNodes()
    {
        // 既定の並び順は「Folders → Plusのショートカット3つ」
        Assert.Equal(
            new[] { TreeNodes.AllRoots }.Concat(TreeNodes.OptionalNodes),
            TreeNodes.AllNodes);
    }

    [Fact]
    public void OptionalNodes_DoesNotContainAllRoots()
    {
        // Foldersは非表示にできない（並び順の指定対象にはなる）
        Assert.DoesNotContain(TreeNodes.AllRoots, TreeNodes.OptionalNodes);
    }

    [Fact]
    public void DefaultVisibleNodes_AreAllOptionalNodes()
    {
        Assert.Equal(TreeNodes.OptionalNodes, TreeNodes.DefaultVisibleNodes);
    }

    [Fact]
    public void AllNodes_HasNoDuplicateKeys()
    {
        // キーは settings.json に保存されるため、重複するとノードの並び順の復元が壊れる
        Assert.Equal(TreeNodes.AllNodes.Count, TreeNodes.AllNodes.Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData(VirtualFolderKind.AllRoots)]
    [InlineData(VirtualFolderKind.Favorites)]
    [InlineData(VirtualFolderKind.Recent)]
    [InlineData(VirtualFolderKind.Frequent)]
    public void GetKind_RoundTripsWithGetKey(VirtualFolderKind kind)
    {
        Assert.Equal(kind, TreeNodes.GetKind(TreeNodes.GetKey(kind)));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("None")]
    [InlineData("RemovedNode")]
    public void GetKind_ReturnsNoneForUnknownKeys(string? key)
    {
        Assert.Equal(VirtualFolderKind.None, TreeNodes.GetKind(key));
    }

    [Fact]
    public void GetEffectiveVisibleNodes_KeepsTheConfiguredNodesInTheDefaultOrder()
    {
        var effective = TreeNodes.GetEffectiveVisibleNodes(
            new[] { TreeNodes.Frequent, TreeNodes.Favorites },
            arePlusFeaturesEnabled: true);

        Assert.Equal(new[] { TreeNodes.Favorites, TreeNodes.Frequent }, effective);
    }

    [Fact]
    public void GetEffectiveVisibleNodes_IgnoreUnknownKeysFromOlderSettings()
    {
        var effective = TreeNodes.GetEffectiveVisibleNodes(
            new[] { "RemovedNode", TreeNodes.Recent },
            arePlusFeaturesEnabled: true);

        Assert.Equal(new[] { TreeNodes.Recent }, effective);
    }

    [Fact]
    public void GetEffectiveVisibleNodes_HidesEverythingForTheFreeVersion()
    {
        // 未購読の間は保存済み設定を無視して1つも出さない（設定自体は残るので購読すれば復活する）
        Assert.Empty(TreeNodes.GetEffectiveVisibleNodes(TreeNodes.AllNodes, arePlusFeaturesEnabled: false));
    }
}
