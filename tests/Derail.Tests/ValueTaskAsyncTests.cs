using AwesomeAssertions;
using Xunit;

namespace Derail.Tests;

public class ValueTaskAsyncTests
{
    [Theory]
    [InlineData(true, "6")]
    [InlineData(false, "missing")]
    public async Task Option_pipeline_continues_as_a_result(bool present, string expected)
    {
        Option<int> option = present ? Option.Some(2) : Option.None;
        ValueTask<string> pipeline = option.MapAsync(x => ValueTask.FromResult(x + 1))
            .BindAsync(x => ValueTask.FromResult(Option.Some(x * 2)))
            .ToResult(() => "missing")
            .Match(x => x.ToString(), error => error);

        (await pipeline).Should().Be(expected);
    }

    [Fact]
    public async Task Pending_predicate_can_reject_a_value_and_skip_later_callbacks()
    {
        var decision = new TaskCompletionSource<bool>();
        ValueTask<Result<int, string>> pipeline = Result.Ok<int, string>(3)
            .EnsureAsync(_ => new ValueTask<bool>(decision.Task), x => $"rejected {x}")
            .TapAsync(_ => throw new InvalidOperationException())
            .MapAsync(ValueTask<int> (int _) => throw new InvalidOperationException());

        pipeline.IsCompleted.Should().BeFalse();
        decision.SetResult(false);
        (await pipeline).Should().Be(Result.Error<int, string>("rejected 3"));
    }

    [Fact]
    public async Task Tap_propagates_callback_cancellation()
    {
        var token = new CancellationToken(canceled: true);
        ValueTask<Result<int, string>> pending = Result.Ok<int, string>(1)
            .TapAsync(_ => ValueTask.FromCanceled(token));

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.AsTask());
        error.CancellationToken.Should().Be(token);
    }

    [Fact]
    public async Task Plain_async_lambda_still_starts_a_Task_pipeline()
    {
        Task<Option<int>> pending = Option.Some(2).MapAsync(async x => { await Task.Yield(); return x + 1; });
        (await pending).Should().Be(Option.Some(3));
    }
}
