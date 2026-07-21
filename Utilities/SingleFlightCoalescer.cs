namespace ParallelScope.Utilities;

/// <summary>
/// 連続したリクエストを1つに統合し、非同期ハンドラを直列に実行する汎用キュー。
/// 実行中に新しいリクエストが来た場合は最新のものだけを保持し、完了後に処理する
/// （途中のリクエストは破棄される = 最新状態のみが反映される）。
/// MainWindowViewModel 内で3箇所重複していた「pending + isRunning」パターンを共通化したもの。
/// </summary>
public sealed class SingleFlightCoalescer<TRequest>
{
    private readonly Func<TRequest, Task> _handleAsync;
    private readonly object _gate = new();
    private TRequest? _pendingRequest;
    private bool _hasPendingRequest;
    private bool _isRunning;

    public SingleFlightCoalescer(Func<TRequest, Task> handleAsync)
    {
        _handleAsync = handleAsync;
    }

    /// <summary>リクエストを投入する。既に実行中なら最新のリクエストで上書きするだけで即座に返る。</summary>
    public void Request(TRequest request)
    {
        lock (_gate)
        {
            _pendingRequest = request;
            _hasPendingRequest = true;

            if (_isRunning)
            {
                return;
            }

            _isRunning = true;
        }

        _ = RunAsync();
    }

    /// <summary>保留中のリクエストが無くなるまでハンドラを順番に実行する。</summary>
    private async Task RunAsync()
    {
        try
        {
            while (true)
            {
                TRequest request;

                lock (_gate)
                {
                    if (!_hasPendingRequest)
                    {
                        _isRunning = false;
                        return;
                    }

                    request = _pendingRequest!;
                    // リクエストがエントリ一覧などの大きなオブジェクトを含む場合に、
                    // 処理完了後もフィールド経由で保持し続けないようクリアする
                    _pendingRequest = default;
                    _hasPendingRequest = false;
                }

                await _handleAsync(request).ConfigureAwait(false);
            }
        }
        finally
        {
            lock (_gate)
            {
                _isRunning = false;
            }
        }
    }
}
