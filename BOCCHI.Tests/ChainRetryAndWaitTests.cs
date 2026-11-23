using System.Collections.Concurrent;
using Ocelot.Chain;
using Ocelot.Chain.Extensions;
using Ocelot.Chain.Middleware.Chain;
using Ocelot.Chain.Thread;
using Xunit;

namespace BOCCHI.Tests;

public class ChainRetryAndWaitTests
{
    // Audit: transient chain failures must retry up to the successful attempt.
    [Fact]
    public async Task RetrySucceedsOnThirdAttempt()
    {
        using FakeMainThread main = new();
        ChainContext context = new("retry", new ServiceProvider(main));
        int attempts = 0;
        IChain chain = new Chain("retry", context.ServiceProvider)
            .UseMiddleware(new RetryChainMiddleware(new QuietLog<ChainRetryAndWaitTests>()) { MaxAttempts = 3, DelayMs = 1 })
            .Then(() => ++attempts < 3 ? StepResult.Failure("retry") : StepResult.Success(), "transient");

        ChainResult result = await chain.ExecuteAsync(context);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, attempts);
        Assert.Equal(3, context.GetData<int>("ChainAttempt"));
    }

    // Audit: persistent chain failures must stop at MaxAttempts.
    [Fact]
    public async Task RetryFailsAtMaximumAttempts()
    {
        using FakeMainThread main = new();
        ChainContext context = new("retry", new ServiceProvider(main));
        int attempts = 0;
        IChain chain = new Chain("retry", context.ServiceProvider)
            .UseMiddleware(new RetryChainMiddleware(new QuietLog<ChainRetryAndWaitTests>()) { MaxAttempts = 2, DelayMs = 1 })
            .Then(() =>
            {
                attempts++;
                return StepResult.Failure("still failing");
            }, "persistent");

        ChainResult result = await chain.ExecuteAsync(context);

        Assert.True(result.IsError);
        Assert.Equal("still failing", result.ErrorMessage);
        Assert.Equal(2, attempts);
        Assert.Equal(2, context.GetData<int>("ChainAttempt"));
    }

    // Audit: WaitUntil must recheck its predicate and finish once it becomes true.
    [Fact]
    public async Task WaitUntilSucceedsWhenPredicateTurnsTrue()
    {
        using FakeMainThread main = new();
        int checks = 0;
        bool allChecksOnMain = true;
        IChain chain = new Chain("wait", new ServiceProvider(main))
            .WaitUntil(_ =>
            {
                allChecksOnMain &= main.IsMainThread;
                return new ValueTask<bool>(++checks == 3);
            }, TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(1));

        ChainResult result = await chain.ExecuteAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(3, checks);
        Assert.True(allChecksOnMain);
    }

    // Audit: WaitUntil must fail instead of waiting forever after its deadline.
    [Fact]
    public async Task WaitUntilFailsAfterTimeout()
    {
        using FakeMainThread main = new();
        IChain chain = new Chain("wait", new ServiceProvider(main))
            .WaitUntil(_ => new ValueTask<bool>(false), TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(1));

        ChainResult result = await chain.ExecuteAsync();

        Assert.True(result.IsError);
        Assert.Contains("WaitUntil timed out", result.ErrorMessage);
    }

    // Audit: a zero WaitUntil timeout must evaluate exactly once rather than wait forever.
    [Fact]
    public async Task WaitUntilWithZeroTimeoutChecksOnceAndFails()
    {
        using FakeMainThread main = new();
        int checks = 0;
        IChain chain = new Chain("wait", new ServiceProvider(main))
            .WaitUntil(_ =>
            {
                checks++;
                return new ValueTask<bool>(false);
            }, TimeSpan.Zero, TimeSpan.FromMilliseconds(1));

        ChainResult result = await chain.ExecuteAsync();

        Assert.True(result.IsError);
        Assert.Equal(1, checks);
    }

    // Audit: cancelling an active WaitUntil must cancel the chain and skip later steps.
    [Fact]
    public async Task WaitUntilCancellationCancelsChain()
    {
        using FakeMainThread main = new();
        using CancellationTokenSource cancellation = new();
        TaskCompletionSource<bool> evaluated = new(TaskCreationOptions.RunContinuationsAsynchronously);
        bool ranAfter = false;
        IChain chain = new Chain("wait", new ServiceProvider(main))
            .WaitUntil(_ =>
            {
                evaluated.TrySetResult(true);
                return new ValueTask<bool>(false);
            }, TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(1))
            .Then(() => ranAfter = true, "after");

        Task<ChainResult> execution = chain.ExecuteAsync(cancellation.Token);
        await evaluated.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();
        ChainResult result = await execution;

        Assert.True(result.IsCanceled);
        Assert.False(ranAfter);
    }

    // Audit: a thrown step must retain its original exception and identify the failing step.
    [Fact]
    public async Task StepExceptionKeepsOriginalExceptionAndStepName()
    {
        using FakeMainThread main = new();
        InvalidOperationException exception = new("original failure");
        IChain chain = new Chain("exception", new ServiceProvider(main))
            .Then((Action)(() => throw exception), "named failing step");

        ChainResult result = await chain.ExecuteAsync();

        Assert.True(result.IsError);
        Assert.Contains("named failing step", result.ErrorMessage);
        Assert.Contains("original failure", result.ErrorMessage);
        Assert.Same(exception, Assert.IsType<InvalidOperationException>(result.Exception).InnerException);
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
