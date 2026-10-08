using System.Diagnostics;
using ParallelScope.ViewModels;

namespace ParallelScope.Tests.TestSupport;

/// <summary>
/// バックグラウンドで進む処理（一覧の読み込み・検索・索引の組み立て）の完了待ち。
/// 固定時間の待機はマシンの負荷次第で不安定になるため、条件が満たされるまで待ち、満たされなければ失敗させる。
/// </summary>
public static class Wait
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(30);

    public static void Until(Func<bool> condition, Func<string> describeFailure)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < Limit)
        {
            if (condition())
            {
                return;
            }

            Thread.Sleep(20);
        }

        Assert.Fail(describeFailure());
    }

    public static void ForItemCount(BrowserTabViewModel tab, int expectedCount)
    {
        Until(
            () => tab.FileItems.Count == expectedCount,
            () => $"一覧が {expectedCount} 件になりませんでした（実際は {tab.FileItems.Count} 件: {Describe(tab)}）");
    }

    /// <summary>件数が変わらない入れ替わり（1件→別の1件）は件数待ちでは素通りしてしまうため、中身と並びで待つ。</summary>
    public static void ForItemNames(BrowserTabViewModel tab, params string[] expectedNames)
    {
        Until(
            () => tab.FileItems.Select(x => x.Name).SequenceEqual(expectedNames),
            () => $"一覧が [{string.Join(", ", expectedNames)}] になりませんでした（実際は [{Describe(tab)}]）");
    }

    private static string Describe(BrowserTabViewModel tab) =>
        string.Join(", ", tab.FileItems.Take(5).Select(x => x.Name)) + (tab.FileItems.Count > 5 ? ", ..." : "");
}
