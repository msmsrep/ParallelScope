using ParallelScope.Utilities;

namespace ParallelScope.Tests.Utilities;

public class PathNormalizerTests
{
    [Theory]
    [InlineData(@"C:\Temp\Foo\", @"C:\Temp\Foo")]
    [InlineData(@"C:\Temp\Foo", @"C:\Temp\Foo")]
    [InlineData(@"C:\Temp\Foo\\", @"C:\Temp\Foo")]
    [InlineData(@"C:\Temp\Bar\..\Foo", @"C:\Temp\Foo")]
    [InlineData(@"C:/Temp/Foo/", @"C:\Temp\Foo")]
    public void Normalize_TrimsTrailingSeparatorAndResolvesRelativeSegments(string input, string expected)
    {
        Assert.Equal(expected, PathNormalizer.Normalize(input));
    }

    [Fact]
    public void Normalize_KeepsDriveRootSeparator()
    {
        // ドライブルートだけは末尾の区切りを落とすと "C:" になり、別の意味（カレントディレクトリ相対）になる
        Assert.Equal(@"C:\", PathNormalizer.Normalize(@"C:\"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_ReturnsEmptyForBlankInput(string input)
    {
        Assert.Equal(string.Empty, PathNormalizer.Normalize(input));
    }

    [Theory]
    [InlineData(VirtualFolders.AllRootsPath, VirtualFolders.AllRootsPath)]
    [InlineData("::favorites::", VirtualFolders.FavoritesPath)]
    [InlineData("::FREQUENT::", VirtualFolders.FrequentPath)]
    public void Normalize_ReturnsCanonicalFormForVirtualPaths(string input, string expected)
    {
        // 仮想パスは Path.GetFullPath に通すと相対パス扱いで壊れるため、正規形をそのまま返す
        Assert.Equal(expected, PathNormalizer.Normalize(input));
    }

    [Theory]
    [InlineData(@"C:\Temp\Foo", @"c:\temp\foo\", true)]
    [InlineData(@"C:\Temp\Foo", @"C:\Temp\Foobar", false)]
    [InlineData("::Favorites::", "::favorites::", true)]
    [InlineData("::Favorites::", "::Frequent::", false)]
    public void AreSame_ComparesNormalizedPathsCaseInsensitively(string left, string right, bool expected)
    {
        Assert.Equal(expected, PathNormalizer.AreSame(left, right));
    }

    [Theory]
    [InlineData(@"C:\Temp", @"C:\Temp", true)]
    [InlineData(@"C:\Temp", @"C:\Temp\Foo", true)]
    [InlineData(@"C:\Temp", @"c:\temp\foo\bar", true)]
    [InlineData(@"C:\", @"C:\Temp", true)]
    [InlineData(@"C:\Temp", @"C:\Temporary", false)]
    [InlineData(@"C:\Temp\Foo", @"C:\Temp", false)]
    [InlineData(@"C:\Temp", @"D:\Temp\Foo", false)]
    public void IsAncestorOrSame_TreatsSeparatorBoundaryStrictly(string ancestor, string target, bool expected)
    {
        // "C:\Temp" が "C:\Temporary" の祖先と誤判定されないこと（単純な前方一致だと誤る）を含めて確認する
        Assert.Equal(expected, PathNormalizer.IsAncestorOrSame(ancestor, target));
    }

    [Theory]
    [InlineData(@"C:\Temp", @"C:\Temp\")]
    [InlineData(@"C:\Temp\", @"C:\Temp\")]
    [InlineData(@"C:\", @"C:\")]
    public void WithTrailingSeparator_AppendsSeparatorOnlyWhenMissing(string input, string expected)
    {
        Assert.Equal(expected, PathNormalizer.WithTrailingSeparator(input));
    }
}
