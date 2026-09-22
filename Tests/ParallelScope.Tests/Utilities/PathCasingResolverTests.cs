using System.IO;
using ParallelScope.Tests.TestSupport;
using ParallelScope.Utilities;

namespace ParallelScope.Tests.Utilities;

/// <summary>
/// 移動先パスの大文字小文字を実際の表記へそろえる処理の確認（一時フォルダに実際のフォルダを作って見る）。
/// </summary>
public class PathCasingResolverTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly string _inner;

    public PathCasingResolverTests()
    {
        _inner = Path.Combine(_temp.Path, "CaseCheck", "InnerFolder");
        Directory.CreateDirectory(_inner);
    }

    public void Dispose() => _temp.Dispose();

    /// <summary>一時フォルダ（表記はテスト側が作ったとおり）より下だけを小文字にしたパス。</summary>
    private string LowerBelowTemp(string path) => _temp.Path + path[_temp.Path.Length..].ToLowerInvariant();

    [Fact]
    public void Resolve_RestoresActualCasingBelowTheRoot()
    {
        var typed = LowerBelowTemp(_inner);

        Assert.Equal(_inner, PathCasingResolver.Resolve(typed, new[] { _temp.Path }));
    }

    // スキャンはルートの表記をそのまま前置きして書き込むので、配下はルートの表記にそろえる
    [Fact]
    public void Resolve_PrefersTheRegisteredRootSpelling()
    {
        var upperRoot = _temp.Path.ToUpperInvariant();
        var typed = _inner.ToLowerInvariant();

        var resolved = PathCasingResolver.Resolve(typed, new[] { upperRoot });

        Assert.Equal(Path.Combine(upperRoot, "CaseCheck", "InnerFolder"), resolved);
    }

    [Fact]
    public void Resolve_ReturnsTheRootSpellingWhenTheRootItselfIsTyped()
    {
        Assert.Equal(_temp.Path, PathCasingResolver.Resolve(_temp.Path.ToLowerInvariant(), new[] { _temp.Path }));
    }

    // キャッシュにそのままの表記で載っているなら、ファイルシステムへは問い合わせない
    [Fact]
    public void Resolve_KeepsPathsKnownToBeExact()
    {
        var typed = LowerBelowTemp(_inner);

        Assert.Equal(typed, PathCasingResolver.Resolve(typed, new[] { _temp.Path }, _ => true));
    }

    [Fact]
    public void Resolve_KeepsTypedSpellingForSegmentsThatCannotBeFound()
    {
        var typed = Path.Combine(LowerBelowTemp(Path.Combine(_temp.Path, "CaseCheck")), "missing");

        Assert.Equal(Path.Combine(_temp.Path, "CaseCheck", "missing"), PathCasingResolver.Resolve(typed, new[] { _temp.Path }));
    }

    [Fact]
    public void Resolve_UppercasesTheDriveLetterOutsideRegisteredRoots()
    {
        var typed = char.ToLowerInvariant(_inner[0]) + _inner[1..];

        Assert.Equal(_inner, PathCasingResolver.Resolve(typed, Array.Empty<string>()));
    }

    [Fact]
    public void Resolve_LeavesVirtualFoldersUntouched()
    {
        var virtualPath = VirtualFolders.RecentPath;

        Assert.Equal(virtualPath, PathCasingResolver.Resolve(virtualPath, new[] { _temp.Path }));
    }
}
