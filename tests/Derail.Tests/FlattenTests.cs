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
}
