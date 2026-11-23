using System.Runtime.CompilerServices;
using FsCheck.Xunit;
using Xunit;

namespace Derail.Tests;

public class RecoveryTests
{
    [Property]
    public bool Recover_ChangesErrorTypeOnlyOnFailure(bool ok, int? value, int error, bool repair)
    {
        var source = ok ? Result.Ok<int?, int>(value) : Result.Error<int?, int>(error);
        var calls = 0;
        var recovered = source.Recover(e =>
        {
            calls++;
            return repair ? Result.Ok<int?, string>(e) : Result.Error<int?, string>($"failed: {e}");
        });
        var expected = ok ? Result.Ok<int?, string>(value)
            : repair ? Result.Ok<int?, string>(error) : Result.Error<int?, string>($"failed: {error}");
        return recovered.Equals(expected) && calls == (ok ? 0 : 1);
    }

    [Property]
    public bool TapError_PreservesResultAndObservesOnlyErrors(bool ok, int value, int? error)
    {
        var source = ok ? Result.Ok<int, int?>(value) : Result.Error<int, int?>(error);
        var observed = new List<int?>();
        return source.TapError(observed.Add).Equals(source) && observed.SequenceEqual(ok ? [] : new[] { error });
    }

    [Property]
    public bool OrElse_EvaluatesFallbackExactlyOnceForNone(bool present, int? value, bool found)
    {
        Option<int?> source = present ? Option.Some(value) : Option.None;
        Option<int?> fallback = found ? Option.Some<int?>(3) : Option.None;
        var calls = 0;
        var actual = source.OrElse(() => { calls++; return fallback; });
        return actual.Equals(present ? source : fallback) && calls == (present ? 0 : 1);
    }

    [Fact]
    public void Default_result_is_not_a_recoverable_error()
    {
        Result<int, string> source = default;
        Assert.Throws<SwitchExpressionException>(() => source.Recover(_ => Result.Ok<int, int>(1)));
        Assert.Throws<SwitchExpressionException>(() => source.TapError(_ => throw new InvalidOperationException()));
    }

    [Fact]
    public async Task Default_option_is_not_a_missing_value()
    {
        Option<int> source = default;
        static Option<int> Unexpected() => throw new InvalidOperationException("unexpected fallback");
        Assert.Throws<SwitchExpressionException>(() => source.OrElse(Unexpected));
        await Assert.ThrowsAsync<SwitchExpressionException>(() => source.OrElseAsync(() => Task.FromResult(Unexpected())));
        await Assert.ThrowsAsync<SwitchExpressionException>(() => source.OrElseAsync(() => ValueTask.FromResult(Unexpected())).AsTask());
        await Assert.ThrowsAsync<SwitchExpressionException>(() => Task.FromResult(source).OrElse(Unexpected));
        await Assert.ThrowsAsync<SwitchExpressionException>(() => ValueTask.FromResult(source).OrElse(Unexpected).AsTask());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Async_present_null_skips_all_recovery_callbacks(bool useValueTask)
    {
        var source = Option.Some<string?>(null);
        var actual = useValueTask
            ? await ValueTask.FromResult(source).OrElseAsync(() => throw new InvalidOperationException())
                .ToResult(() => "missing").TapErrorAsync(_ => throw new InvalidOperationException())
                .RecoverAsync(ValueTask<Result<string?, int>> (string _) => throw new InvalidOperationException())
            : await Task.FromResult(source).OrElseAsync(() => throw new InvalidOperationException())
                .ToResult(() => "missing").TapErrorAsync(_ => throw new InvalidOperationException())
                .RecoverAsync(Task<Result<string?, int>> (string _) => throw new InvalidOperationException());
        Assert.Equal(Result.Ok<string?, int>(null), actual);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Observer_fault_propagates_without_running_recovery(bool useValueTask)
    {
        var observation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var error = Result.Error<int, string>("declined");
        static Result<int, int> Unexpected(string _) => throw new InvalidOperationException("unexpected recovery");
        var pipeline = useValueTask
            ? ValueTask.FromResult(error).TapErrorAsync(_ => new ValueTask(observation.Task))
                .Recover(Unexpected).AsTask()
            : Task.FromResult(error).TapErrorAsync(_ => observation.Task)
                .Recover(Unexpected);
        Assert.False(pipeline.IsCompleted);
        var fault = new IOException("observer failed");
        observation.SetException(fault);
        Assert.Same(fault, await Assert.ThrowsAsync<IOException>(() => pipeline));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Async_cache_miss_observes_and_recovers_only_a_typed_error(bool useValueTask)
    {
        Option<int> missing = Option.None;
        var observed = new List<string>();
        var result = useValueTask
            ? await ValueTask.FromResult(missing).OrElseAsync(() => ValueTask.FromResult(missing))
                .ToResult(() => "missing").TapError(observed.Add)
                .RecoverAsync(e => ValueTask.FromResult(Result.Error<int, int>(e.Length)))
            : await Task.FromResult(missing).OrElseAsync(() => Task.FromResult(missing))
                .ToResult(() => "missing").TapError(observed.Add)
                .RecoverAsync(e => Task.FromResult(Result.Error<int, int>(e.Length)));
        Assert.Equal(["missing"], observed);
        Assert.Equal(Result.Error<int, int>(7), result);
    }
}
