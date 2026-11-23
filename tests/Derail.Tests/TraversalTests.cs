using System.Runtime.CompilerServices;
using FsCheck.Xunit;
using Xunit;

namespace Derail.Tests;

public class TraversalTests
{
    [Property]
    public bool Option_traversals_agree_with_the_ordered_values_or_absence(int[] inputs, Func<int, int?> f)
    {
        var projected = inputs.Select(f).ToArray();
        Option<int> Map(int x) => Option.FromNullable(f(x));
        var mapped = inputs.Select(Map).ToArray();
        var outcomes = new[] { Option.Traverse(inputs, Map), Option.Sequence(mapped), Option.Traverse(mapped, x => x) };
        return outcomes.All(o => o.Match(xs => projected.All(x => x.HasValue) && xs.Select(x => (int?)x).SequenceEqual(projected),
            () => projected.Any(x => !x.HasValue)));
    }

    [Property]
    public bool Result_traversals_agree_with_the_first_error_or_ordered_values(int[] inputs, Func<int, bool> reject)
    {
        Result<int, int> Map(int x) => reject(x) ? Result.Error<int, int>(x) : Result.Ok<int, int>(x);
        var mapped = inputs.Select(Map).ToArray();
        var errors = inputs.Where(reject).ToArray();
        var outcomes = new[] { Result.Traverse(inputs, Map), Result.Sequence(mapped), Result.Traverse(mapped, x => x) };
        return outcomes.All(r => r.Match(xs => errors.Length == 0 && xs.SequenceEqual(inputs),
            error => errors.Length > 0 && error == errors[0]));
    }

    [Property]
    public bool Validation_traversals_agree_with_all_errors_or_ordered_values(int[] inputs, Func<int, bool> reject)
    {
        Validation<int, int> Map(int x) => reject(x) ? Validation.Invalid<int, int>(x, ~x) : Validation.Valid<int, int>(x);
        var mapped = inputs.Select(Map).ToArray();
        var errors = inputs.Where(reject).SelectMany(x => new[] { x, ~x }).ToArray();
        var outcomes = new[] { Validation.Traverse(inputs, Map), Validation.Sequence(mapped), Validation.Traverse(mapped, x => x) };
        return outcomes.All(v => v.Match(xs => errors.Length == 0 && xs.SequenceEqual(inputs), es => es.SequenceEqual(errors)));
    }

    [Theory]
    [InlineData("Option", 2)]
    [InlineData("Result", 2)]
    [InlineData("Validation", 3)]
    public void Traverse_reads_once_and_disposes_after_the_last_required_item(string primitive, int expectedVisits)
    {
        var reads = 0;
        var visits = new List<int>();
        var disposed = false;
        IEnumerable<int> Source()
        {
            Assert.Equal(1, ++reads);
            try
            {
                for (int i = 1; i <= 3; i++)
                {
                    Assert.True(i <= expectedVisits, "Read past the first failure");
                    yield return i;
                }
            }
            finally { disposed = true; }
        }
        bool Accept(int x) { visits.Add(x); return x != 2; }
        switch (primitive)
        {
            case "Option": Option.Traverse(Source(), x => Accept(x) ? Option.Some(x) : Option.None); break;
            case "Result": Result.Traverse(Source(), x => Accept(x) ? Result.Ok<int, int>(x) : Result.Error<int, int>(x)); break;
            case "Validation": Validation.Traverse(Source(), x => Accept(x) ? Validation.Valid<int, int>(x) : Validation.Invalid<int, int>(x)); break;
        }
        Assert.Equal(Enumerable.Range(1, expectedVisits), visits);
        Assert.True(disposed);
    }

    [Fact]
    public void Empty_inputs_succeed_without_invoking_the_mapper()
    {
        int[] empty = [];
        static Option<int> Missing(int _) => throw new InvalidOperationException();
        static Result<int, string> Fail(int _) => throw new InvalidOperationException();
        static Validation<int, string> Reject(int _) => throw new InvalidOperationException();

        Assert.True(Option.Traverse(empty, Missing) is Some<IReadOnlyList<int>>({ Count: 0 }));
        Assert.True(Result.Traverse(empty, Fail) is Ok<IReadOnlyList<int>>({ Count: 0 }));
        Assert.True(Validation.Traverse(empty, Reject) is Valid<IReadOnlyList<int>>({ Count: 0 }));
    }

    [Fact]
    public void Successes_are_immutable_snapshots_including_null_payloads()
    {
        string?[] input = [null, "before"];
        var snapshots = new[]
        {
            Option.Traverse(input, Option.Some).GetValueOrDefault([]),
            Result.Traverse(input, Result.Ok<string?, int>).GetValueOrDefault([]),
            Validation.Traverse(input, Validation.Valid<string?, int>).GetValueOrDefault([]),
        };
        input[1] = "after";
        foreach (var values in snapshots)
        {
            Assert.Equal(new string?[] { null, "before" }, values);
            if (values is IList<string?> list)
                Assert.Throws<NotSupportedException>(() => list[0] = "changed");
        }
    }

    [Fact]
    public void Uninitialized_results_are_rejected_only_if_reached()
    {
        Result<int, int> missing = default;
        Assert.Throws<SwitchExpressionException>(() => Option.Sequence<int>([default]));
        Assert.True(Option.Sequence<int>([Option.None, default]).IsNone);
        Assert.Throws<SwitchExpressionException>(() => Result.Sequence([missing]));
        Assert.Equal(Result.Error<IReadOnlyList<int>, int>(1), Result.Sequence([Result.Error<int, int>(1), missing]));
        Assert.Throws<SwitchExpressionException>(() => Validation.Sequence([Validation.Invalid<int, int>(1), default]));
    }
}
