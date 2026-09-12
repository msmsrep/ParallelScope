using ParallelScope.Utilities;

namespace ParallelScope.Tests.Utilities;

/// <summary>
/// 検索語の照合器の確認。部分一致は従来どおり <see cref="NameSearchMatcher"/> と同じ判定になり、
/// 正規表現は大文字小文字を区別せず、壊れた式は <see cref="NameSearchPattern.IsValid"/> で判別できること。
/// </summary>
public class NameSearchPatternTests
{
    [Theory]
    [InlineData("report", "Report2026.xlsx", true)]
    [InlineData("REPORT", "report2026.xlsx", true)]
    [InlineData("2027", "report2026.xlsx", false)]
    public void SubstringModeMatchesTheSameWayAsBefore(string query, string name, bool expected)
    {
        var pattern = NameSearchPattern.Create(query, useRegex: false);

        Assert.False(pattern.IsRegex);
        Assert.True(pattern.IsValid);
        Assert.Equal(expected, pattern.Matches(name));
        Assert.Equal(NameSearchMatcher.Contains(name, query), pattern.Matches(name));
    }

    [Theory]
    [InlineData(@"^report\d+\.xlsx$", "report2026.xlsx", true)]
    [InlineData(@"^report\d+\.xlsx$", "report.xlsx", false)]
    [InlineData(@"\.(jpg|png)$", "photo.PNG", true)]
    [InlineData("^a.c$", "abc", true)]
    [InlineData("^a.c$", "abcd", false)]
    public void RegexModeMatchesTheWholePattern(string query, string name, bool expected)
    {
        var pattern = NameSearchPattern.Create(query, useRegex: true);

        Assert.True(pattern.IsRegex);
        Assert.True(pattern.IsValid);
        Assert.Equal(expected, pattern.Matches(name));
    }

    // 通常の検索と同じく大文字小文字は区別しない（索引が大文字へ寄せた名前しか持たないため）
    [Theory]
    [InlineData("readme", "README.TXT")]
    [InlineData("README", "readme.txt")]
    [InlineData("[a-z]+", "ABC")]
    public void RegexModeIgnoresCase(string query, string name)
    {
        Assert.True(NameSearchPattern.Create(query, useRegex: true).Matches(name));
    }

    // 打ちかけの正規表現。呼び出し側はここで検索そのものを見送る
    [Theory]
    [InlineData("(")]
    [InlineData("[a-")]
    [InlineData("*.txt")]
    public void BrokenRegexIsReportedAsInvalid(string query)
    {
        var pattern = NameSearchPattern.Create(query, useRegex: true);

        Assert.True(pattern.IsRegex);
        Assert.False(pattern.IsValid);
        Assert.False(pattern.Matches("anything.txt"));
    }

    // 索引側は大文字へ寄せ済みの名前を渡してくるので、キャッシュDB側と同じ判定にならなければならない
    [Theory]
    [InlineData(@"^rep.*\.txt$", "Report.txt")]
    [InlineData("note", "NOTES.md")]
    public void FoldedMatchingAgreesWithRawMatching(string query, string name)
    {
        foreach (var useRegex in new[] { true, false })
        {
            var pattern = NameSearchPattern.Create(query, useRegex);

            Assert.Equal(pattern.Matches(name), pattern.MatchesFolded(NameSearchMatcher.FoldAscii(name)));
        }
    }
}
