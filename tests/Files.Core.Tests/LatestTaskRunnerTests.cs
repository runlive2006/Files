using Files.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.Core.Tests;

[TestClass]
public sealed class LatestTaskRunnerTests
{
    [TestMethod]
    public async Task RunAsync_ReturnsWorkResult()
    {
        using var runner = new LatestTaskRunner();

        int result = await runner.RunAsync(_ => 42);

        Assert.AreEqual(42, result);
    }

    [TestMethod]
    public async Task RunAsync_NewCallCancelsPrevious()
    {
        using var runner = new LatestTaskRunner();
        var firstStarted = new ManualResetEventSlim(initialState: false);

        // 第一次调用会被卡住，直到第二次调用把它取消
        Task<int> first = runner.RunAsync(token =>
        {
            firstStarted.Set();

            // 用可取消的等待，使取消能立即解除阻塞（否则要等满超时）
            token.WaitHandle.WaitOne(TimeSpan.FromSeconds(5));
            token.ThrowIfCancellationRequested();
            return 1;
        });

        Assert.IsTrue(firstStarted.Wait(TimeSpan.FromSeconds(5)));

        Task<int> second = runner.RunAsync(_ => 2);

        int secondResult = await second;
        int firstResult = await first;

        Assert.AreEqual(2, secondResult, "后发起的调用应返回自己的结果。");
        Assert.AreEqual(0, firstResult, "被取代的调用应返回 default。");
    }

    [TestMethod]
    public async Task RunAsync_PropagatesNonCancellationExceptions()
    {
        using var runner = new LatestTaskRunner();

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => runner.RunAsync<int>(_ => throw new InvalidOperationException("boom")));
    }

    [TestMethod]
    public async Task RunAsync_AfterDispose_Throws()
    {
        var runner = new LatestTaskRunner();
        runner.Dispose();

        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(
            () => runner.RunAsync(_ => 1));
    }

    [TestMethod]
    public async Task Cancel_PendingWork_ReturnsDefault()
    {
        using var runner = new LatestTaskRunner();
        var started = new ManualResetEventSlim(initialState: false);

        Task<int> pending = runner.RunAsync(token =>
        {
            started.Set();

            // 一直阻塞到被取消，保证 Cancel 一定发生在工作完成之前
            token.WaitHandle.WaitOne(TimeSpan.FromSeconds(5));
            token.ThrowIfCancellationRequested();
            return 7;
        });

        Assert.IsTrue(started.Wait(TimeSpan.FromSeconds(5)));
        runner.Cancel();

        Assert.AreEqual(0, await pending);
    }
}
