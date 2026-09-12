using ParallelScope.Utilities;

namespace ParallelScope.Tests.Utilities;

/// <summary>
/// 検索結果の絞り込みに使う部分一致判定。キャッシュDB側の `Name LIKE '%語%'` と結果が一致することが要件で、
/// SQLiteのLIKEは「ASCIIの大文字小文字だけ」を無視する。
/// </summary>
public class NameSearchMatcherTests
{
    [Theory]
    [InlineData("report.txt", "report", true)]
    [InlineData("report.txt", "port", true)]
    [InlineData("report.txt", ".txt", true)]
    [InlineData("report.txt", "REPORT", true)]
    [InlineData("REPORT.TXT", "report", true)]
    [InlineData("report.txt", "reports", false)]
    [InlineData("report.txt", "xyz", false)]
    [InlineData("rep", "report", false)]
    public void Contains_MatchesSubstringsIgnoringAsciiCase(string name, string query, bool expected)
    {
        Assert.Equal(expected, NameSearchMatcher.Contains(name, query));
    }

    [Fact]
    public void Contains_TreatsAnEmptyQueryAsMatchingEverything()
    {
        Assert.True(NameSearchMatcher.Contains("report.txt", string.Empty));
    }

    // SQLiteのLIKEはASCII以外の大文字小文字を畳まない。ここで畳んでしまうと、
    // 打ちながら絞り込んだ場合と、同じ検索語でDBを引き直した場合とで件数が変わってしまう
    [Theory]
    [InlineData("CAFÉ.txt", "café", false)]
    [InlineData("CAFÉ.txt", "CAFÉ", true)]
    [InlineData("ΔΕΛΤΑ.txt", "δελτα", false)]
    [InlineData("ΔΕΛΤΑ.txt", "ΔΕΛΤΑ", true)]
    public void Contains_DoesNotFoldNonAsciiCase(string name, string query, bool expected)
    {
        Assert.Equal(expected, NameSearchMatcher.Contains(name, query));
    }

    [Theory]
    [InlineData("設計書.xlsx", "設計", true)]
    [InlineData("設計書.xlsx", "仕様", false)]
    public void Contains_MatchesNonAsciiSubstrings(string name, string query, bool expected)
    {
        Assert.Equal(expected, NameSearchMatcher.Contains(name, query));
    }
}
