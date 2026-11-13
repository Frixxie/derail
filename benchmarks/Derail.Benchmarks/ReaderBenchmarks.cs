using BenchmarkDotNet.Attributes;

namespace Derail.Benchmarks;

[MemoryDiagnoser]
public class ReaderConstructionBenchmarks
{
    [Params(1, 8, 32)]
    public int Steps { get; set; }

    [GlobalSetup]
    public void Verify()
    {
        if (DelegateChain()(42) != ReaderMap().Run(42)
            || ReaderMap().Run(42) != ReaderBind().Run(42)
            || ReaderResultMap().Run(42).GetValueOrDefault(-1) != 42 + Steps)
            throw new InvalidOperationException("Reader construction outputs differ");
    }

    [Benchmark(Baseline = true)]
    public Func<int, int> DelegateChain()
    {
        Func<int, int> run = static env => env;
        for (var i = 0; i < Steps; i++)
        {
            var previous = run;
            run = env => previous(env) + 1;
        }
        return run;
    }

    [Benchmark]
    public Reader<int, int> ReaderMap()
    {
        var reader = Reader.Ask<int>();
        for (var i = 0; i < Steps; i++) reader = reader.Map(static x => x + 1);
        return reader;
    }

    [Benchmark]
    public Reader<int, int> ReaderBind()
    {
        var reader = Reader.Ask<int>();
        for (var i = 0; i < Steps; i++) reader = reader.Bind(static x => Reader.Pure<int, int>(x + 1));
        return reader;
    }

    [Benchmark]
    public ReaderResult<int, int, string> ReaderResultMap()
    {
        var reader = ReaderResult.FromReader<int, int, string>(Reader.Ask<int>());
        for (var i = 0; i < Steps; i++) reader = reader.Map(static x => x + 1);
        return reader;
    }
}

[MemoryDiagnoser]
public class ReaderExecutionBenchmarks
{
    [Params(1, 8, 32)]
    public int Steps { get; set; }

    // Mutable input prevents a compile-time constant baseline.
    public int Environment { get; set; } = 42;

    private Func<int, int> _delegate = null!;
    private Reader<int, int> _map = null!;
    private Reader<int, int> _bind = null!;
    private ReaderResult<int, int, string> _result = null!;

    [GlobalSetup]
    public void Setup()
    {
        var construction = new ReaderConstructionBenchmarks { Steps = Steps };
        _delegate = construction.DelegateChain();
        _map = construction.ReaderMap();
        _bind = construction.ReaderBind();
        _result = construction.ReaderResultMap();
        if (Direct() != DelegateChain() || Direct() != ReaderMap() || Direct() != ReaderBind()
            || Direct() != ReaderResultMap().GetValueOrDefault(-1))
            throw new InvalidOperationException("Reader execution outputs differ");
    }

    [Benchmark(Baseline = true)]
    public int Direct()
    {
        var value = Environment;
        for (var i = 0; i < Steps; i++) value++;
        return value;
    }

    [Benchmark]
    public int DelegateChain() => _delegate(Environment);

    [Benchmark]
    public int ReaderMap() => _map.Run(Environment);

    [Benchmark]
    public int ReaderBind() => _bind.Run(Environment);

    [Benchmark]
    public Result<int, string> ReaderResultMap() => _result.Run(Environment);
}

[MemoryDiagnoser]
public class ReaderResultShortCircuitBenchmarks
{
    [Params(1, 8, 32)]
    public int Steps { get; set; }

    [Params(false, true)]
    public bool Fail { get; set; }

    private ReaderResult<bool, int, string> _reader = null!;

    [GlobalSetup]
    public void Setup()
    {
        _reader = new(env => env ? Result.Error<int, string>("failed") : Result.Ok<int, string>(0));
        for (var i = 0; i < Steps; i++)
            _reader = _reader.Bind(static x => ReaderResult.Pure<bool, int, string>(x + 1));
        if (!Direct().Equals(ReaderBind())) throw new InvalidOperationException("Result outputs differ");
    }

    [Benchmark(Baseline = true)]
    public Result<int, string> Direct()
    {
        if (Fail) return Result.Error<int, string>("failed");
        var value = 0;
        for (var i = 0; i < Steps; i++) value++;
        return Result.Ok<int, string>(value);
    }

    [Benchmark]
    public Result<int, string> ReaderBind() => _reader.Run(Fail);
}
