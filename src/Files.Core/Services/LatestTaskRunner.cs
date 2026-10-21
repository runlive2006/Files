namespace Files.Core.Services;

/// <summary>
/// "只保留最后一次请求"的异步执行器。
/// </summary>
/// <remarks>
/// 用于导航这类会被高频触发的操作：用户连续点面包屑、或在地址栏里快速输入时，
/// 只有最后一次导航应该产生结果，之前的必须被取消。
/// <para>
/// 若不取消，慢速磁盘上的旧请求会在新请求之后返回，把界面覆盖成用户已经离开的目录 ——
/// 这是文件管理器最典型的"导航错乱"缺陷。
/// </para>
/// </remarks>
public sealed class LatestTaskRunner : IDisposable
{
    private readonly object _gate = new();
    private CancellationTokenSource? _current;
    private bool _disposed;

    /// <summary>
    /// 执行一次工作，并取消此前尚未完成的工作。
    /// </summary>
    /// <typeparam name="T">结果类型。</typeparam>
    /// <param name="work">工作内容，会在线程池上执行。</param>
    /// <returns>
    /// 工作结果；若被更新的一次调用取消，则返回 <see langword="default"/>。
    /// </returns>
    public async Task<T?> RunAsync<T>(Func<CancellationToken, T> work)
    {
        ArgumentNullException.ThrowIfNull(work);

        CancellationTokenSource cts;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            _current?.Cancel();
            _current?.Dispose();
            _current = new CancellationTokenSource();
            cts = _current;
        }

        try
        {
            // ConfigureAwait(true)：调用方通常是 UI 线程，需要回到原上下文更新界面
            return await Task.Run(() => work(cts.Token), cts.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return default;
        }
    }

    /// <summary>取消当前尚未完成的工作。</summary>
    public void Cancel()
    {
        lock (_gate)
        {
            _current?.Cancel();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _current?.Cancel();
            _current?.Dispose();
            _current = null;
        }
    }
}
