using BenchmarkDotNet.Attributes;
using Derail;

namespace Derail.Benchmarks;

[MemoryDiagnoser]
public class ResultBenchmarks
{
    [Params("42", "x", "0", "-1")]
    public string Input { get; set; } = "";

    private static Result<int, string> Parse(string input) =>
        int.TryParse(input, out var value)
            ? Result.Ok<int, string>(value)
             : Result.Error<int, string>("not a number");

    private static Result<int, string> Positive(int value) =>
        value > 0 ? Result.Ok<int, string>(value) : Result.Error<int, string>("not positive");

    [GlobalSetup]
    public void Verify()
    {
        if (TryParse_Baseline() != Result_MapBind() || TryParse_Baseline() != Result_Linq())
            throw new InvalidOperationException("Result benchmark outputs differ");
    }

    [Benchmark(Baseline = true)]
    public int TryParse_Baseline() =>
        int.TryParse(Input, out var value) && value > 0 ? value * 2 : -1;

    [Benchmark]
    public int Result_MapBind() =>
        Parse(Input)
            .Bind(Positive)
            .Map(x => x * 2)
            .GetValueOrDefault(-1);

    [Benchmark]
    public int Result_Linq() =>
        (from x in Parse(Input)
         from positive in Positive(x)
         select positive * 2).GetValueOrDefault(-1);
}
