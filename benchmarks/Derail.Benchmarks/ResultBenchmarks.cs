using BenchmarkDotNet.Attributes;
using Derail;

namespace Derail.Benchmarks;

[MemoryDiagnoser]
public class ResultBenchmarks
{
    [Params("42", "x")]
    public string Input { get; set; } = "";

    private static Result<int, string> Parse(string input) =>
        int.TryParse(input, out var value)
            ? Result.Ok<int, string>(value)
            : Result.Error<int, string>("not a number");

    [Benchmark(Baseline = true)]
    public int TryParse_Baseline() =>
        int.TryParse(Input, out var value) && value > 0 ? value * 2 : -1;

    [Benchmark]
    public int Result_MapBind() =>
        Parse(Input)
            .Bind(x => x > 0 ? Result.Ok<int, string>(x) : Result.Error<int, string>("not positive"))
            .Map(x => x * 2)
            .GetValueOrDefault(-1);

    [Benchmark]
    public int Result_Linq() =>
        (from x in Parse(Input)
         select x * 2).GetValueOrDefault(-1);
}
