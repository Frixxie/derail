using BenchmarkDotNet.Attributes;

namespace Derail.Benchmarks;

[MemoryDiagnoser]
public class ValidationSequenceBenchmarks
{
    [Params(1, 8, 32, 1024)]
    public int Count { get; set; }

    /// <summary>Zero means all valid, one means all invalid, two means alternating.</summary>
    [Params(0, 1, 2)]
    public int FailureEvery { get; set; }

    private Validation<int, int>[] _validations = null!;

    [GlobalSetup]
    public void Setup()
    {
        _validations = Enumerable.Range(0, Count)
            .Select(i => FailureEvery != 0 && i % FailureEvery == 0
                ? Validation.Invalid<int, int>(i)
                : Validation.Valid<int, int>(i))
            .ToArray();
        var expected = Direct();
        var actual = Sequence();
        if (!expected.Match(
            values => actual.Match(other => values.SequenceEqual(other), _ => false),
            errors => actual.Match(_ => false, other => errors.SequenceEqual(other))))
            throw new InvalidOperationException("Validation outputs differ");
    }

    [Benchmark(Baseline = true)]
    public Validation<IReadOnlyList<int>, int> Direct()
    {
        var values = new List<int>();
        var errors = new List<int>();
        foreach (var validation in _validations)
        {
            switch (validation)
            {
                case Valid<int> valid when errors.Count == 0:
                    values.Add(valid.Value);
                    break;
                case Invalid<int> invalid:
                    errors.AddRange(invalid.Errors);
                    break;
            }
        }
        return errors.Count == 0
            ? Validation.Valid<IReadOnlyList<int>, int>(values.ToArray())
            : Validation.Invalid<IReadOnlyList<int>, int>(errors[0], errors.ToArray().AsSpan()[1..]);
    }

    [Benchmark]
    public Validation<IReadOnlyList<int>, int> Sequence() => Validation.Sequence(_validations);
}
