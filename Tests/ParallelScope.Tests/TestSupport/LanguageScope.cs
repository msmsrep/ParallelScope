using ParallelScope.Utilities;

namespace ParallelScope.Tests.TestSupport;

/// <summary>
/// 表示言語はプロセス全体で1つの静的な状態のため、テストの間だけ切り替えて元へ戻す。
/// 言語を切り替えるテストクラスは<see cref="LanguageCollection.Name"/>のコレクションに入れて直列実行する。
/// </summary>
public sealed class LanguageScope : IDisposable
{
    private readonly AppLanguageSetting _previous;

    public LanguageScope(AppLanguageSetting language)
    {
        _previous = AppLanguage.Current;
        AppLanguage.Apply(language);
    }

    public void Dispose()
    {
        AppLanguage.Apply(_previous);
    }
}

/// <summary>言語を切り替えるテストクラスをまとめるコレクション（同一コレクション内は並列実行されない）。</summary>
[CollectionDefinition(LanguageCollection.Name)]
public class LanguageCollection
{
    public const string Name = "Language";
}
