using ParallelScope.Utilities;

namespace ParallelScope.Tests.Utilities;

public class BackgroundWorkGateTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task RunAsync_ReturnsTheResultOfTheWork()
    {
        var gate = new BackgroundWorkGate(2);

        Assert.Equal(42, await gate.RunAsync(() => 42));
    }

    [Fact]
    public async Task RunAsync_DoesNotRunMoreWorkThanTheLimitAtOnce()
    {
        const int maxConcurrency = 2;
        var gate = new BackgroundWorkGate(maxConcurrency);
        var release = new ManualResetEventSlim();
        var running = 0;
        var observedPeak = 0;

        var tasks = Enumerable.Range(0, 10)
            .Select(_ => gate.RunAsync(() =>
            {
                var current = Interlocked.Increment(ref running);
                InterlockedMax(ref observedPeak, current);

                // 全ての枠が埋まるまで解放しないことで、同時実行数の上限を確実に観測する
                release.Wait(Timeout);

                Interlocked.Decrement(ref running);
            }))
            .ToList();

        // 上限ぶんが走り出したことを確認してから解放する
        var deadline = DateTime.UtcNow + Timeout;
        while (Volatile.Read(ref running) < maxConcurrency && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(10);
        }

        release.Set();
        await Task.WhenAll(tasks).WaitAsync(Timeout);

        Assert.Equal(maxConcurrency, observedPeak);
    }

    [Fact]
    public async Task RunAsync_ReleasesTheSlotWhenTheWorkThrows()
    {
        var gate = new BackgroundWorkGate(1);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => gate.RunAsync(() => throw new InvalidOperationException()));

        // 枠が返っていなければここで止まる
        Assert.Equal(1, await gate.RunAsync(() => 1).WaitAsync(Timeout));
    }

    private static void InterlockedMax(ref int target, int value)
    {
        var current = Volatile.Read(ref target);
        while (value > current)
        {
            var previous = Interlocked.CompareExchange(ref target, value, current);
            if (previous == current)
            {
                return;
            }

            current = previous;
        }
    }
}
