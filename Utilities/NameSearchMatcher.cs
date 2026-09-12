namespace ParallelScope.Utilities;

/// <summary>
/// ファイル名に検索語が含まれるかの判定。キャッシュDB側の `Name LIKE '%語%'` と結果が一致するよう、
/// SQLiteのLIKEと同じ「ASCIIの大文字小文字だけを無視する」規則で比較する。
/// </summary>
/// <remarks>
/// .NET の <see cref="StringComparison.OrdinalIgnoreCase"/> はアクセント付きラテン文字やギリシャ文字なども
/// 畳むため、そちらを使うとDBを引き直したときと結果が食い違う（打ちながら絞り込んだ場合と、
/// 同じ検索語を貼り付けた場合とで件数が変わってしまう）。
/// </remarks>
public static class NameSearchMatcher
{
    public static bool Contains(string name, string query)
    {
        if (query.Length == 0)
        {
            return true;
        }

        var lastStart = name.Length - query.Length;
        for (var start = 0; start <= lastStart; start++)
        {
            if (MatchesAt(name, query, start))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesAt(string name, string query, int start)
    {
        for (var i = 0; i < query.Length; i++)
        {
            if (FoldAscii(name[start + i]) != FoldAscii(query[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>ASCIIの小文字だけを大文字へ寄せる（SQLiteのLIKEと同じ畳み方）。</summary>
    public static char FoldAscii(char value)
    {
        return value is >= 'a' and <= 'z' ? (char)(value - ('a' - 'A')) : value;
    }

    /// <summary>文字列全体を <see cref="FoldAscii(char)"/> の規則で畳む。</summary>
    public static string FoldAscii(string value)
    {
        return string.Create(value.Length, value, static (destination, source) =>
        {
            for (var i = 0; i < source.Length; i++)
            {
                destination[i] = FoldAscii(source[i]);
            }
        });
    }
}
