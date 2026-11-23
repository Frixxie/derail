using BenchmarkDotNet.Attributes;
using Derail;

namespace Derail.Benchmarks;

[MemoryDiagnoser]
public class ResultTapBenchmarks
{
    [Params(true, false)]
    public bool Success { get; set; }

    private Result<int, string> input;
    private int observed;
    private Action<int> observe = null!;

    [GlobalSetup]
    public void Setup()
    {
        input = Success ? Result.Ok<int, string>(42) : Result.Error<int, string>("upstream");
        observe = value => observed = value;

        var direct = Direct();
        var expected = observed;
        observed = 0;
        if (!Tap().Equals(direct) || observed != expected)
            throw new InvalidOperationException("Tap benchmark outcomes differ");
    }

    [Benchmark(Baseline = true)]
    public Result<int, string> Direct()
    {
        if (input is Ok<int>(var value))
            observed = value;
        return input;
    }

    [Benchmark]
    public Result<int, string> Tap() => input.Tap(observe);
}

[MemoryDiagnoser]
public class ResultEnsureBenchmarks
{
    // Positive passes, negative is rejected, null represents an upstream failure.
    [Params(42, -1, null)]
    public int? Value { get; set; }

    private Result<int, int> input;

    [GlobalSetup]
    public void Setup()
    {
        input = Value is int value ? Result.Ok<int, int>(value) : Result.Error<int, int>(0);
        if (!Ensure().Equals(Direct()))
            throw new InvalidOperationException("Ensure benchmark outcomes differ");
    }

    [Benchmark(Baseline = true)]
    public Result<int, int> Direct() =>
        input is Ok<int>(var value) && value <= 0 ? Result.Error<int, int>(value) : input;

    [Benchmark]
    public Result<int, int> Ensure() => input.Ensure(static x => x > 0, static x => x);
}

[MemoryDiagnoser]
public class ResultAsyncGuardBenchmarks
{
    [Params(42, -1, null)]
    public int? Value { get; set; }

    private Result<int, int> input;
    private int observed;
    private Func<int, Task> observe = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        input = Value is int value ? Result.Ok<int, int>(value) : Result.Error<int, int>(0);
        observe = x => { observed = x; return Task.CompletedTask; };

        var direct = await Direct();
        var expected = observed;
        observed = 0;
        if (!(await EnsureAsyncThenTapAsync()).Equals(direct) || observed != expected)
            throw new InvalidOperationException("Async guard benchmark outcomes differ");
    }

    private static Task<bool> IsPositive(int value) => Task.FromResult(value > 0);

    [Benchmark(Baseline = true)]
    public async Task<Result<int, int>> Direct()
    {
        if (input is Ok<int>(var value))
        {
            if (!await IsPositive(value))
                return Result.Error<int, int>(value);
            await observe(value);
        }
        return input;
    }

    [Benchmark]
    public Task<Result<int, int>> EnsureAsyncThenTapAsync() =>
        input.EnsureAsync(IsPositive, static x => x).TapAsync(observe);
}
