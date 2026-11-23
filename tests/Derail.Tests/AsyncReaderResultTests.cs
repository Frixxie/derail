using FsCheck.Xunit;
using Xunit;

namespace Derail.Tests;

public class AsyncReaderResultTests
{
    [Fact]
    public async Task Query_is_lazy_and_each_run_supplies_its_own_environment_and_token()
    {
        using var first = new CancellationTokenSource();
        using var second = new CancellationTokenSource();
        var calls = new List<(string, CancellationToken)>();
        var read = new AsyncReaderResult<string, int, string>((env, token) =>
        {
            calls.Add((env, token));
            return Task.FromResult(Result.Ok<int, string>(env.Length));
        });
        var query = from env in AsyncReaderResult.Ask<string, string>()
                    from length in read
                    select $"{env}:{length}";
        var workflow = query.Local<string[]>(parts => parts[0]);

        Assert.Empty(calls);
        Assert.Equal(Result.Ok<string, string>("abc:3"), await workflow.Run(["abc"], first.Token));
        Assert.Equal(Result.Ok<string, string>("x:1"), await workflow.Run(["x"], second.Token));
        Assert.Equal([("abc", first.Token), ("x", second.Token)], calls);
    }

    [Fact]
    public async Task Zip_waits_for_left_and_recovery_receives_original_environment_and_token()
    {
        using var cancellation = new CancellationTokenSource();
        var gate = new TaskCompletionSource<Result<int, string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var left = new AsyncReaderResult<int, int, string>((_, _) => gate.Task);
        var right = new AsyncReaderResult<int, int, string>((_, _) => throw new InvalidOperationException("unexpected right"));
        var workflow = left.Zip(right, (a, b) => a + b)
            .Ensure(_ => throw new InvalidOperationException("unexpected predicate"), _ => "rejected")
            .MapError(e => e.Length)
            .Recover(e => new AsyncReaderResult<int, int, bool>((env, token) =>
            {
                Assert.Equal(cancellation.Token, token);
                return Task.FromResult(Result.Ok<int, bool>(env + e));
            }));

        var pending = workflow.Run(20, cancellation.Token);
        Assert.False(pending.IsCompleted);
        gate.SetResult(Result.Error<int, string>("missing"));
        Assert.Equal(Result.Ok<int, bool>(27), await pending);
    }

    [Fact]
    public async Task Cancellation_before_run_skips_even_a_lifted_sync_reader()
    {
        var token = new CancellationToken(canceled: true);
        var reader = new ReaderResult<int, int, string>(_ => throw new InvalidOperationException("unexpected read"));
        var pending = AsyncReaderResult.FromReaderResult(reader).Run(0, token);
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(token, error.CancellationToken);
        Assert.True(pending.IsCanceled);
    }

    [Fact]
    public async Task Cancellation_during_IO_is_not_recovered_as_a_domain_error()
    {
        using var cts = new CancellationTokenSource();
        var read = new AsyncReaderResult<int, int, string>(async (_, token) =>
        {
            Assert.Equal(cts.Token, token);
            await Task.Delay(Timeout.Infinite, token);
            return Result.Ok<int, string>(1);
        });
        var pending = read.Recover(_ => AsyncReaderResult.Pure<int, int, string>(0)).Run(1, cts.Token);
        cts.Cancel();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(cts.Token, error.CancellationToken);
    }

    [Fact]
    public async Task Faults_pass_through_recovery()
    {
        var error = new IOException("connection lost");
        var read = new AsyncReaderResult<int, int, string>((_, _) => throw error);
        var pending = read.Recover(_ => AsyncReaderResult.Pure<int, int, string>(0)).Run(1, TestContext.Current.CancellationToken);
        Assert.Same(error, await Assert.ThrowsAsync<IOException>(() => pending));
    }

    private static AsyncReaderResult<int, int, string> Lookup(Func<int, int?> lookup) =>
        new((env, _) => Task.FromResult(Option.FromNullable(lookup(env)).ToResult(() => "missing")));

    [Theory]
    [InlineData(2, 5)]
    [InlineData(-2, 1)]
    public async Task Lifted_reader_runs_only_at_execution_and_guard_checks_combined_value(int env, int sum)
    {
        var calls = 0;
        var sync = new ReaderResult<int, int, string>(value => { calls++; return Result.Ok<int, string>(value); });
        var workflow = AsyncReaderResult.FromReaderResult(sync)
            .Zip(AsyncReaderResult.FromResult<int, int, string>(Result.Ok<int, string>(3)), (a, b) => a + b)
            .Ensure(value => value >= 3, value => $"too small: {value}");
        Assert.Equal(0, calls);
        var result = await workflow.Run(env, TestContext.Current.CancellationToken);
        Assert.Equal(sum >= 3 ? Result.Ok<int, string>(sum) : Result.Error<int, string>($"too small: {sum}"), result);
        Assert.Equal(1, calls);
    }

    [Property]
    public async Task<bool> Map_identity_and_composition(int env, Func<int, int?> lookup, Func<int, int> f, Func<int, int> g)
    {
        var source = Lookup(lookup);
        return (await source.Map(x => x).Run(env)).Equals(await source.Run(env))
            && (await source.Map(f).Map(g).Run(env)).Equals(await source.Map(x => g(f(x))).Run(env));
    }

    [Property]
    public async Task<bool> Bind_identities_and_associativity(int env, int value, Func<int, int?> lookup, Func<int, int?> f, Func<int, int?> g)
    {
        var source = Lookup(lookup);
        AsyncReaderResult<int, int, string> First(int x) => Lookup(e => f(e + x));
        AsyncReaderResult<int, int, string> Second(int x) => Lookup(e => g(e - x));
        return (await AsyncReaderResult.Pure<int, int, string>(value).Bind(First).Run(env)).Equals(await First(value).Run(env))
            && (await source.Bind(AsyncReaderResult.Pure<int, int, string>).Run(env)).Equals(await source.Run(env))
            && (await source.Bind(First).Bind(Second).Run(env)).Equals(await source.Bind(x => First(x).Bind(Second)).Run(env));
    }
}
