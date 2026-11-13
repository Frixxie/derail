using BenchmarkDotNet.Attributes;

namespace Derail.Benchmarks;

[MemoryDiagnoser]
public class WriterBenchmarks
{
    [Params(1, 8, 32, 1024)]
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
