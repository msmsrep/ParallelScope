using ParallelScope.Tests.TestSupport;
using ParallelScope.Utilities;

namespace ParallelScope.Tests.Utilities;

[Collection(LanguageCollection.Name)]
public class VirtualFoldersTests
{
    [Theory]
    [InlineData(VirtualFolders.AllRootsPath, VirtualFolderKind.AllRoots)]
    [InlineData(VirtualFolders.FavoritesPath, VirtualFolderKind.Favorites)]
    [InlineData(VirtualFolders.FrequentPath, VirtualFolderKind.Frequent)]
    [InlineData("::folders::", VirtualFolderKind.AllRoots)]
    [InlineData(VirtualFolders.RecentPath, VirtualFolderKind.Recent)]
    [InlineData(@"C:\Temp", VirtualFolderKind.None)]
    [InlineData("", VirtualFolderKind.None)]
    [InlineData(null, VirtualFolderKind.None)]
    public void GetKind_IdentifiesVirtualNodesCaseInsensitively(string? path, VirtualFolderKind expected)
    {
        Assert.Equal(expected, VirtualFolders.GetKind(path));
    }

    [Theory]
    [InlineData(VirtualFolders.AllRootsPath, true)]
    [InlineData(VirtualFolders.FavoritesPath, true)]
    [InlineData(@"C:\Temp", false)]
    public void IsVirtual_MatchesAnyVirtualKind(string path, bool expected)
    {
        Assert.Equal(expected, VirtualFolders.IsVirtual(path));
    }

    [Theory]
    [InlineData(VirtualFolders.AllRootsPath, true)]
    [InlineData(VirtualFolders.FavoritesPath, false)]
    public void IsAllRoots_MatchesOnlyAllRoots(string path, bool expected)
    {
        Assert.Equal(expected, VirtualFolders.IsAllRoots(path));
    }

    [Fact]
    public void GetCanonicalPath_ReturnsNullForRealPaths()
    {
        Assert.Null(VirtualFolders.GetCanonicalPath(@"C:\Temp"));
        Assert.Equal(VirtualFolders.FavoritesPath, VirtualFolders.GetCanonicalPath("::FAVORITES::"));
    }

    [Theory]
    [InlineData(VirtualFolderKind.Favorites, VirtualFolders.FavoritesDisplayNameKey)]
    [InlineData(VirtualFolderKind.Frequent, VirtualFolders.FrequentDisplayNameKey)]
    [InlineData(VirtualFolderKind.Recent, VirtualFolders.RecentDisplayNameKey)]
    [InlineData(VirtualFolderKind.AllRoots, VirtualFolders.AllRootsDisplayNameKey)]
    [InlineData(VirtualFolderKind.None, VirtualFolders.AllRootsDisplayNameKey)]
    public void GetDisplayNameKey_ReturnsNodeLabelKey(VirtualFolderKind kind, string expected)
    {
        Assert.Equal(expected, VirtualFolders.GetDisplayNameKey(kind));
    }

    [Fact]
    public void GetDisplayName_FollowsTheCurrentLanguage()
    {
        using (new LanguageScope(AppLanguageSetting.English))
        {
            Assert.Equal("★ Favorites", VirtualFolders.GetDisplayName(VirtualFolderKind.Favorites));
        }

        using (new LanguageScope(AppLanguageSetting.Japanese))
        {
            Assert.Equal("★ お気に入り", VirtualFolders.GetDisplayName(VirtualFolderKind.Favorites));
        }
    }

    [Fact]
    public void VirtualPaths_ContainCharactersThatCannotAppearInRealPaths()
    {
        // 実在パスと衝突しないことが仮想パスの前提なので、Windowsのパスに使えない ":" を含むことを保証する
        foreach (var path in new[] { VirtualFolders.AllRootsPath, VirtualFolders.FavoritesPath, VirtualFolders.FrequentPath })
        {
            Assert.Contains(':', path);
        }
    }
}
