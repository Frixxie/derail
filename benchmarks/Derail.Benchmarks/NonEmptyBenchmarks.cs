using System.Collections.Immutable;
using BenchmarkDotNet.Attributes;

namespace Derail.Benchmarks;

[MemoryDiagnoser]
public class NonEmptyMapBenchmarks
{
    [Params(1, 8, 32, 1024)]
    public int Count { get; set; }

    private int[] _values = null!;
    private NonEmpty<int> _nonEmpty = null!;

    [GlobalSetup]
    public void Setup()
    {
        _values = Enumerable.Range(0, Count).ToArray();
        _nonEmpty = new(_values[0], _values.AsSpan()[1..]);
        if (!ArrayMap().SequenceEqual(NonEmptyMap())) throw new InvalidOperationException("Mapped values differ");
    }

    [Benchmark(Baseline = true)]
    public int[] ArrayMap()
    {
        var mapped = new int[_values.Length];
        for (var i = 0; i < mapped.Length; i++) mapped[i] = _values[i] + 1;
        return mapped;
    }

    [Benchmark]
    public NonEmpty<int> NonEmptyMap() => _nonEmpty.Map(static x => x + 1);
}

[MemoryDiagnoser]
public class NonEmptyConstructionBenchmarks
{
    [Params(1, 8, 32, 1024)]
    public int Count { get; set; }

    private int[] _values = null!;
    private NonEmpty<int>[] _singletons = null!;

    [GlobalSetup]
    public void Setup()
    {
        _values = Enumerable.Range(0, Count).ToArray();
        _singletons = _values.Select(x => new NonEmpty<int>(x)).ToArray();
        if (!HeadAndTail().SequenceEqual(AppendSingletons())
            || !FromEnumerable().Match(values => values.SequenceEqual(_values), () => false)
            || !SnapshotThenConstruct().Match(values => values.SequenceEqual(_values), () => false))
            throw new InvalidOperationException("Constructed values differ");
    }

    [Benchmark(Baseline = true)]
    public NonEmpty<int> HeadAndTail() => new(_values[0], _values.AsSpan()[1..]);

    [Benchmark]
    public Option<NonEmpty<int>> FromEnumerable() => NonEmpty.FromEnumerable(_values);

    // The original conversion strategy: snapshot the input, then snapshot its tail.
    [Benchmark]
    public Option<NonEmpty<int>> SnapshotThenConstruct()
    {
        var items = _values.ToImmutableArray();
        return items.IsEmpty ? Option.None : Option.Some(new NonEmpty<int>(items[0], items.AsSpan()[1..]));
    }

    [Benchmark]
    public NonEmpty<int> AppendSingletons()
    {
        var values = _singletons[0];
        for (var i = 1; i < _singletons.Length; i++) values = values.Append(_singletons[i]);
        return values;
    }
}
