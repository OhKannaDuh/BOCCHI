using System.Collections.Concurrent;
using Ocelot.Chain;
using Ocelot.Chain.Extensions;
using Ocelot.Chain.Thread;
using Xunit;

namespace BOCCHI.Tests;

public class ChainThreadTests
{
    [Fact]
    public async Task StepAfterDelayRunsOnStartThread()
    {
        using FakeMainThread main = new();
        ServiceProvider services = new(main);
        bool? secondStepOnMain = null;

        IChain chain = new Chain("test", services)
            .Then(async () => await Task.Delay(20), "delay")
            .Then(() => secondStepOnMain = main.IsMainThread, "check");

        ChainResult result = await main.InvokeAsync(() => chain.ExecuteAsync());

        Assert.True(result.IsSuccess);
        Assert.True(secondStepOnMain);
    }

    [Fact]
    public async Task FailingStepStopsChain()
    {
        using FakeMainThread main = new();
        bool ranAfter = false;

        IChain chain = new Chain("test", new ServiceProvider(main))
            .Then(() => StepResult.Failure("boom"), "fail")
            .Then(() => ranAfter = true, "after");

        ChainResult result = await chain.ExecuteAsync();

        Assert.False(result.IsSuccess);
        Assert.False(ranAfter);
    }

    [Fact]
    public async Task CancelledTokenCancelsChain()
    {
        using FakeMainThread main = new();
        using CancellationTokenSource cts = new();
        cts.Cancel();

        ChainResult result = await new Chain("test", new ServiceProvider(main))
            .Then(() => StepResult.Success(), "never")
            .ExecuteAsync(cts.Token);

        Assert.True(result.IsCanceled);
    }

    private sealed class ServiceProvider(IMainThread main) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(IMainThread) ? main : null;
    }

    private sealed class FakeMainThread : IMainThread, IDisposable
    {
        private readonly BlockingCollection<Action> queue = new();

        private readonly Thread thread;

        public FakeMainThread()
        {
            thread = new Thread(() =>
            {
                foreach (Action action in queue.GetConsumingEnumerable())
                {
                    action();
                }
            }) { IsBackground = true };
            thread.Start();
        }

        public bool IsMainThread => Thread.CurrentThread == thread;

        public Task SwitchAsync(CancellationToken ct = default) => InvokeAsync(() => Task.CompletedTask, ct);

        public Task<T> InvokeAsync<T>(Func<Task<T>> func, CancellationToken ct = default)
        {
            if (IsMainThread)
            {
                return func();
            }

            TaskCompletionSource<Task<T>> started = new();
            queue.Add(() => started.SetResult(func()));
            return started.Task.Unwrap();
        }

        public Task InvokeAsync(Func<Task> func, CancellationToken ct = default)
            => InvokeAsync(async () =>
            {
                await func();
                return true;
            }, ct);

        public void Post(Action action) => queue.Add(action);

        public void Dispose() => queue.CompleteAdding();
    }
}
