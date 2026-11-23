using FsCheck.Xunit;
using Xunit;

namespace Derail.Tests;

public class FlattenTests
{
    [Property]
    public bool Option_preserves_absence_at_either_level(bool outerPresent, bool innerPresent, int? value)
    {
        Option<int?> inner = innerPresent ? Option.Some(value) : Option.None;
        Option<Option<int?>> outer = outerPresent ? Option.Some(inner) : Option.None;
        var actual = outer.Flatten();
        return actual.Equals(outer.Bind(x => x)) && actual.Match(
            x => outerPresent && innerPresent && x == value, () => !outerPresent || !innerPresent);
    }

    [Property]
    public bool Result_preserves_first_error(bool outerOk, bool innerOk, int value)
    {
        var inner = innerOk ? Result.Ok<int, string>(value) : Result.Error<int, string>("inner");
        var outer = outerOk ? Result.Ok<Result<int, string>, string>(inner) : Result.Error<Result<int, string>, string>("outer");
        var actual = outer.Flatten();
        return actual.Equals(outer.Bind(x => x)) && actual.Match(
            x => outerOk && innerOk && x == value,
            e => !outerOk ? e == "outer" : !innerOk && e == "inner");
    }

    [Property]
    public bool Reader_agrees_with_bind_in_the_supplied_environment(int env, Func<int, int> f, Func<int, int> g)
    {
        var nested = new Reader<int, Reader<int, int>>(a => new(b => f(a) + g(b)));
        var actual = nested.Flatten().Run(env);
        return actual == nested.Bind(x => x).Run(env) && actual == f(env) + g(env);
    }

    [Fact]
    public void Reader_defers_both_reads_until_each_run()
    {
        var reads = new List<string>();
        var nested = new Reader<string, Reader<string, string>>(outer =>
        {
            reads.Add($"outer:{outer}");
            return new(inner => { reads.Add($"inner:{inner}"); return inner; });
        });
        var flattened = nested.Flatten();
        Assert.Empty(reads);
        Assert.Equal("a", flattened.Run("a"));
        Assert.Equal("b", flattened.Run("b"));
        Assert.Equal(["outer:a", "inner:a", "outer:b", "inner:b"], reads);
    }

    [Property]
    public bool Writer_preserves_value_and_outer_to_inner_event_order(int value, int[] outerEvents, int[] innerEvents)
    {
        var inner = new Writer<int, int>(value, innerEvents);
        var outer = new Writer<Writer<int, int>, int>(inner, outerEvents);
        var actual = outer.Flatten();
        var bound = outer.Bind(x => x);
        return actual.Value == value && actual.Value == bound.Value && actual.Events.SequenceEqual(bound.Events)
            && actual.Events.SequenceEqual(outerEvents.Concat(innerEvents));
    }
}
