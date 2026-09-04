using System.Text.RegularExpressions;

namespace ParallelScope.Utilities;

/// <summary>
/// 検索語と、その照合方法（部分一致 or 正規表現）をひとまとめにしたもの。
/// キャッシュDB・ファイル名索引・打ちながらの絞り込みが同じ判定を共有するために通す。
/// </summary>
/// <remarks>
/// 正規表現でも判定対象はASCIIの大文字へ寄せた名前（<see cref="NameSearchMatcher.FoldAscii(string)"/>）。
/// ファイル名索引はこの形でしか名前を持っていないため、キャッシュDB側も同じ形へ寄せてから
/// 照合しないと、索引の有効・無効で結果が食い違う。あわせて大文字小文字は区別しない。
/// </remarks>
public sealed class NameSearchPattern
{
    /// <summary>1件あたりの照合に許す時間。書き方次第で総当たりが爆発しうるため上限を設ける。</summary>
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    private readonly Regex? _regex;

    /// <summary>部分一致のときの、ASCIIの大文字へ寄せた検索語（索引側の走査で使う）。</summary>
    private readonly string _foldedText;

    private NameSearchPattern(string text, bool isRegex, Regex? regex)
    {
        Text = text;
        IsRegex = isRegex;
        _regex = regex;
        _foldedText = isRegex ? string.Empty : NameSearchMatcher.FoldAscii(text);
    }

    /// <summary>入力された検索語（前後の空白は呼び出し側で落とした状態）。</summary>
    public string Text { get; }

    /// <summary>正規表現として解釈するか。</summary>
    public bool IsRegex { get; }

    /// <summary>照合に使えるか（正規表現として壊れていないか）。</summary>
    public bool IsValid => !IsRegex || _regex is not null;

    /// <summary>検索語から照合器を作る。正規表現として壊れている場合も返し、<see cref="IsValid"/> で判別させる。</summary>
    public static NameSearchPattern Create(string text, bool useRegex)
    {
        if (!useRegex)
        {
            return new NameSearchPattern(text, false, null);
        }

        try
        {
            // 判定対象は大文字へ寄せた名前なので、IgnoreCase を付けないと小文字を書いた検索語が当たらない。
            // CultureInvariant はロケール依存の畳み込み（トルコ語のI等）で結果が変わらないようにするため
            return new NameSearchPattern(
                text, true, new Regex(text, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, MatchTimeout));
        }
        catch (ArgumentException)
        {
            return new NameSearchPattern(text, true, null);
        }
    }

    /// <summary>名前（キャッシュDBから読んだそのままの形）が検索語に一致するか。</summary>
    public bool Matches(string name)
    {
        return IsRegex
            ? MatchesFolded(NameSearchMatcher.FoldAscii(name))
            : NameSearchMatcher.Contains(name, Text);
    }

    /// <summary>ASCIIの大文字へ寄せ済みの名前が検索語に一致するか（ファイル名索引用）。</summary>
    public bool MatchesFolded(ReadOnlySpan<char> foldedName)
    {
        if (_regex is null)
        {
            // 壊れた正規表現は何にも当たらない扱い（呼び出し側は IsValid で先に弾く）
            return !IsRegex && foldedName.IndexOf(_foldedText.AsSpan()) >= 0;
        }

        try
        {
            return _regex.IsMatch(foldedName);
        }
        catch (RegexMatchTimeoutException)
        {
            // 1件のために全体を止めない。打ち切った行は不一致として扱う
            return false;
        }
    }
}
