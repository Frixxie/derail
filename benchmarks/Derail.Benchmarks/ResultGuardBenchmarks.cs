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
