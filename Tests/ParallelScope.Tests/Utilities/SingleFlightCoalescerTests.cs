using ParallelScope.Utilities;

namespace ParallelScope.Tests.Utilities;

public class SingleFlightCoalescerTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Request_InvokesHandlerWithTheRequest()
    {
        var handled = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var coalescer = new SingleFlightCoalescer<int>(request =>
        {
            handled.TrySetResult(request);
            return Task.CompletedTask;
        });

        coalescer.Request(42);

        Assert.Equal(42, await handled.Task.WaitAsync(Timeout));
    }

    [Fact]
    public async Task Request_WhileRunning_KeepsOnlyTheLatestRequest()
    {
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondHandled = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handledRequests = new List<int>();

        var coalescer = new SingleFlightCoalescer<int>(async request =>
        {
            lock (handledRequests)
            {
                handledRequests.Add(request);
            }

            if (request == 1)
            {
                firstStarted.SetResult();
                await releaseFirst.Task;
                return;
            }

            secondHandled.TrySetResult(request);
        });

        coalescer.Request(1);
        await firstStarted.Task.WaitAsync(Timeout);

        // 1件目の実行中に来た2・3件目は最新（3）だけが残り、2は破棄される
        coalescer.Request(2);
        coalescer.Request(3);
        releaseFirst.SetResult();

        Assert.Equal(3, await secondHandled.Task.WaitAsync(Timeout));

        lock (handledRequests)
        {
            Assert.Equal(new[] { 1, 3 }, handledRequests);
        }
    }

    [Fact]
    public async Task Request_AfterHandlerThrows_StillProcessesNewRequests()
    {
        // ハンドラが落ちても実行中フラグが戻らないと以後リクエストが一切処理されなくなるため、その回復を確認する
        var firstHandled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondHandled = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        var coalescer = new SingleFlightCoalescer<int>(request =>
        {
            if (request == 1)
            {
                firstHandled.SetResult();
                throw new InvalidOperationException("handler failed");
            }

            secondHandled.TrySetResult(request);
            return Task.CompletedTask;
        });

        coalescer.Request(1);
        await firstHandled.Task.WaitAsync(Timeout);

        coalescer.Request(2);

        Assert.Equal(2, await secondHandled.Task.WaitAsync(Timeout));
    }

    [Fact]
    public async Task Request_RunsHandlersSerially()
    {
        var running = 0;
        var maxConcurrency = 0;
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var coalescer = new SingleFlightCoalescer<int>(async request =>
        {
            maxConcurrency = Math.Max(maxConcurrency, Interlocked.Increment(ref running));
            await Task.Delay(10);
            Interlocked.Decrement(ref running);

            if (request == 2)
            {
                completed.TrySetResult();
            }
        });

        coalescer.Request(1);
        coalescer.Request(2);
        await completed.Task.WaitAsync(Timeout);

        Assert.Equal(1, maxConcurrency);
    }
}
