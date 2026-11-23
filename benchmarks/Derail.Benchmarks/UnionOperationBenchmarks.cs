using BenchmarkDotNet.Attributes;

namespace Derail.Benchmarks;

[MemoryDiagnoser]
public class UnionOperationBenchmarks
{
    [Params(true, false)] public bool Present { get; set; }
    private Option<int> _option;
    private Result<int, int> _result;
    private int? _nullable;

    [GlobalSetup]
    public void Setup()
    {
        _nullable = Present ? 42 : null;
        _option = Option.FromNullable(_nullable);
        _result = _option.ToResult(() => -1);
        var expected = _nullable + 1;
        var expectedResult = expected.HasValue ? Result.Ok<int, int>(43) : Result.Error<int, int>(-1);
        if (!OptionMap().Equals(Option.FromNullable(expected)) || !OptionBind().Equals(OptionMap()) ||
            !ResultMap().Equals(expectedResult) || !ResultBind().Equals(expectedResult) ||
            NullableMap() != expected || new[] { OptionMatch(), OptionPattern(), ResultMatch(), ResultPattern() }.Any(x => x != NullableMatch()))
            throw new InvalidOperationException("Operation outputs differ");
    }

    [Benchmark] public int? NullableMap() => _nullable + 1;
    [Benchmark] public Option<int> OptionMap() => _option.Map(static x => x + 1);
    [Benchmark] public Result<int, int> ResultMap() => _result.Map(static x => x + 1);
    [Benchmark] public Option<int> OptionBind() => _option.Bind(static x => Option.Some(x + 1));
    [Benchmark] public Result<int, int> ResultBind() => _result.Bind(static x => Result.Ok<int, int>(x + 1));
    [Benchmark] public int NullableMatch() => _nullable ?? -1;
    [Benchmark] public int OptionMatch() => _option.Match(static x => x, static () => -1);
    [Benchmark] public int OptionPattern() => _option switch { Some<int>(var x) => x, None => -1 };
    [Benchmark] public int ResultMatch() => _result.Match(static x => x, static e => e);
    [Benchmark] public int ResultPattern() => _result switch { Ok<int>(var x) => x, Error<int>(var e) => e };
}
