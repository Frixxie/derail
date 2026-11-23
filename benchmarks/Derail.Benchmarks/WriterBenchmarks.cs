using BenchmarkDotNet.Attributes;

namespace Derail.Benchmarks;

[MemoryDiagnoser]
public class WriterBenchmarks
{
    [Params(1, 8, 32, 1024, 4096)]
    public int Count { get; set; }

    private int[] _events = null!;

    [GlobalSetup]
    public void Setup()
    {
        _events = Enumerable.Range(0, Count).ToArray();
        var expected = Direct();
        foreach (var actual in new[] { Bind(), Bulk() })
            if (actual.Value != expected.Value || !actual.Events.SequenceEqual(expected.Events))
                throw new InvalidOperationException("Writer outputs differ");
    }

    [Benchmark(Baseline = true)]
    public Writer<int, int> Direct()
    {
        var events = new List<int>(Count);
        var value = 0;
        for (var i = 0; i < Count; i++) events.Add(value++);
        return new(value, events);
    }

    [Benchmark]
    public Writer<int, int> Bind()
    {
        var writer = Writer.Pure<int, int>(0);
        for (var i = 0; i < Count; i++) writer = writer.Bind(static x => new Writer<int, int>(x + 1, [x]));
        return writer;
    }

    // Bulk receives precomputed events: it isolates snapshot cost, not event computation.
    [Benchmark]
    public Writer<int, int> Bulk() => new(Count, _events);
}

[MemoryDiagnoser]
public class WriterReadBenchmarks
{
    [Params(1024, 4096)] public int Count { get; set; }
    private const int Batch = 32;
    private Writer<int, int> _unread = null!;
    private Writer<int, int> _cached = null!;
    private readonly Writer<int, int>[] _fresh = new Writer<int, int>[Batch];

    [GlobalSetup]
    public void Setup()
    {
        _unread = new WriterBenchmarks { Count = Count }.Bind();
        _cached = _unread.Map(static x => x);
        if (!_cached.Events.SequenceEqual(Enumerable.Range(0, Count))) throw new InvalidOperationException("Events differ");
    }

    // Each copy shares the immutable event tree but has its own, still-empty flatten cache.
    [IterationSetup(Target = nameof(FirstRead))]
    public void Reset()
    {
        for (var i = 0; i < Batch; i++) _fresh[i] = _unread.Map(static x => x);
    }

    [Benchmark(OperationsPerInvoke = Batch)]
    public int FirstRead()
    {
        var sum = 0;
        foreach (var writer in _fresh) sum += writer.Events[^1];
        return sum;
    }

    [Benchmark] public int CachedRead() => _cached.Events[^1];
}
