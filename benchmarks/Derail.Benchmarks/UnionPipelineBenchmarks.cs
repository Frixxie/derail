using BenchmarkDotNet.Attributes;
using CSharpFunctionalExtensions;
using Cfe = CSharpFunctionalExtensions.Result;
using Fluent = FluentResults.Result;

namespace Derail.Benchmarks;

[MemoryDiagnoser]
public class UnionPipelineBenchmarks
{
    // Negative: initial error. Zero: dependent step rejects. Positive: success.
    [Params(-1, 0, 7)] public int Input { get; set; }

    [GlobalSetup]
    public void Verify()
    {
        var expected = Imperative();
        if (DerailPipeline() != expected || CfePipeline() != expected || FluentPipeline() != expected)
            throw new InvalidOperationException("Pipeline outputs differ");
    }

    [Benchmark(Baseline = true)]
    public int Imperative()
    {
        if (Input < 0) return -"negative".Length;
        var doubled = Input * 2;
        return doubled == 0 ? -"zero".Length : doubled + 1;
    }

    [Benchmark]
    public int DerailPipeline() =>
        (Input < 0 ? Result.Error<int, string>("negative") : Result.Ok<int, string>(Input))
        .Map(static x => x * 2)
        .Bind(static x => x == 0 ? Result.Error<int, string>("zero") : Result.Ok<int, string>(x + 1))
        .Match(static x => x, static e => -e.Length);

    [Benchmark]
    public int CfePipeline() =>
        (Input < 0 ? Cfe.Failure<int>("negative") : Cfe.Success(Input))
        .Map(static x => x * 2)
        .Bind(static x => x == 0 ? Cfe.Failure<int>("zero") : Cfe.Success(x + 1))
        .Match(static x => x, static e => -e.Length);

    [Benchmark]
    public int FluentPipeline()
    {
        var outcome = (Input < 0 ? Fluent.Fail<int>("negative") : Fluent.Ok(Input))
            .Map(static x => x * 2)
            .Bind(static x => x == 0 ? Fluent.Fail<int>("zero") : Fluent.Ok(x + 1));
        return outcome.IsSuccess ? outcome.Value : -outcome.Errors[0].Message.Length;
    }
}
