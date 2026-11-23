using System.Threading.Tasks.Sources;
using Xunit;

namespace Derail.Tests;

public class AsyncExecutionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Pending_input_does_not_post_back_to_the_callers_context(bool useValueTask)
    {
        var source = new TaskCompletionSource<Option<int>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var context = new RecordingContext();
        var previous = SynchronizationContext.Current;
        Task<Option<int>> mapped;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            mapped = useValueTask
                ? new ValueTask<Option<int>>(source.Task).Map(x => x + 1).AsTask()
                : source.Task.Map(x => x + 1);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        source.SetResult(Option.Some(1));
        Assert.Equal(Option.Some(2), await mapped);
        Assert.Equal(0, context.Posts);
    }

    private sealed class RecordingContext : SynchronizationContext
    {
        public int Posts;

        public override void Post(SendOrPostCallback callback, object? state)
        {
            Interlocked.Increment(ref Posts);
            base.Post(callback, state);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Canceled_input_skips_callback_and_preserves_token(bool useValueTask)
    {
        var token = new CancellationToken(canceled: true);
        static int Unexpected(int _) => throw new InvalidOperationException("Callback ran");
        var mapped = useValueTask
            ? ValueTask.FromCanceled<Result<int, string>>(token).Map(Unexpected).AsTask()
            : Task.FromCanceled<Result<int, string>>(token).Map(Unexpected);

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mapped);
        Assert.Equal(token, exception.CancellationToken);
        Assert.True(mapped.IsCanceled);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Callback_fault_reaches_caller_as_the_original_exception(bool useValueTask)
    {
        var error = new InvalidOperationException("Callback failed");
        var option = Option.Some(1);
        var mapped = useValueTask
            ? option.MapAsync(_ => ValueTask.FromException<int>(error)).AsTask()
            : option.MapAsync(_ => Task.FromException<int>(error));

        Assert.Same(error, await Assert.ThrowsAsync<InvalidOperationException>(() => mapped));
    }

    [Fact]
    public async Task ValueTask_pipeline_consumes_each_source_once()
    {
        var input = new SingleUseSource<Option<int>>(Option.Some(2));
        var output = new SingleUseSource<int>(4);
        var result = await new ValueTask<Option<int>>(input, 0).MapAsync(_ => new ValueTask<int>(output, 0));

        Assert.Equal(Option.Some(4), result);
    }

    private sealed class SingleUseSource<T>(T value) : IValueTaskSource<T>
    {
        private bool _consumed;
        public ValueTaskSourceStatus GetStatus(short token) => ValueTaskSourceStatus.Succeeded;
        public T GetResult(short token)
        {
            if (_consumed) throw new InvalidOperationException("ValueTask consumed twice");
            _consumed = true;
            return value;
        }
        public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags) =>
            throw new InvalidOperationException("Source already completed");
    }
}
