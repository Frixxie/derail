using BenchmarkDotNet.Attributes;

namespace Derail.Benchmarks;

[MemoryDiagnoser]
public class TraversalBenchmarks
{
    [Params(32, 1024)] public int Count { get; set; }
    [Params(false, true)] public bool RejectOdd { get; set; }

    private int[] _inputs = null!;
    private Option<int>[] _options = null!;
    private Result<int, int>[] _results = null!;

    private bool Accept(int x) => !RejectOdd || x % 2 == 0;
    private Option<int> Find(int x) => Accept(x) ? Option.Some(x) : Option.None;
    private Result<int, int> Parse(int x) => Accept(x) ? Result.Ok<int, int>(x) : Result.Error<int, int>(x);
    private Validation<int, int> Check(int x) => Accept(x) ? Validation.Valid<int, int>(x) : Validation.Invalid<int, int>(x);

    [GlobalSetup]
    public void Setup()
    {
        _inputs = Enumerable.Range(0, Count).ToArray();
        _options = _inputs.Select(Find).ToArray();
        _results = _inputs.Select(Parse).ToArray();

        bool AllValues(IReadOnlyList<int> values) => !RejectOdd && values.SequenceEqual(_inputs);
        bool FirstError(int error) => RejectOdd && error == 1;
        if (!OptionSequence().Match(AllValues, () => RejectOdd) || !OptionTraverse().Match(AllValues, () => RejectOdd)
            || !ResultSequence().Match(AllValues, FirstError) || !ResultTraverse().Match(AllValues, FirstError)
            || !ValidationTraverse().Match(AllValues, errors => RejectOdd && errors.SequenceEqual(_inputs.Where(x => x % 2 != 0))))
            throw new InvalidOperationException("Unexpected traversal outcome");
    }

    [Benchmark] public Option<IReadOnlyList<int>> OptionSequence() => Option.Sequence(_options);
    [Benchmark] public Option<IReadOnlyList<int>> OptionTraverse() => Option.Traverse(_inputs, Find);
    [Benchmark] public Result<IReadOnlyList<int>, int> ResultSequence() => Result.Sequence(_results);
    [Benchmark] public Result<IReadOnlyList<int>, int> ResultTraverse() => Result.Traverse(_inputs, Parse);
    [Benchmark] public Validation<IReadOnlyList<int>, int> ValidationTraverse() => Validation.Traverse(_inputs, Check);
}
