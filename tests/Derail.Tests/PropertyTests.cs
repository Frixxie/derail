using FsCheck.Xunit;
using Derail;

namespace Derail.Tests;

public class ResultProperties
{
    private static Result<int, string> Make(bool ok, int value, string error) =>
        ok ? Result.Ok<int, string>(value) : Result.Error<int, string>(error);

    private static Result<int, string> Half(int x) =>
        x % 2 == 0 ? Result.Ok<int, string>(x / 2) : Result.Error<int, string>("odd");

    [Property]
    public bool Map_Identity(bool ok, int value, string error) =>
        Make(ok, value, error).Map(x => x).Equals(Make(ok, value, error));

    [Property]
    public bool Map_Composition(bool ok, int value, string error) =>
        Make(ok, value, error).Map(x => x + 1).Map(x => x * 3)
            .Equals(Make(ok, value, error).Map(x => (x + 1) * 3));

    [Property]
    public bool Bind_LeftIdentity(int value) =>
        Result.Ok<int, string>(value).Bind(Half).Equals(Half(value));

    [Property]
    public bool Bind_RightIdentity(bool ok, int value, string error) =>
        Make(ok, value, error).Bind(Result.Ok<int, string>).Equals(Make(ok, value, error));

    [Property]
    public bool Bind_Associativity(bool ok, int value, string error) =>
        Make(ok, value, error).Bind(Half).Bind(Half)
            .Equals(Make(ok, value, error).Bind(x => Half(x).Bind(Half)));
}

public class OptionProperties
{
    private static Option<int> Make(bool some, int value) =>
        some ? Option.Some(value) : Option.None;

    [Property]
    public bool Map_Identity(bool some, int value) =>
        Make(some, value).Map(x => x).Equals(Make(some, value));

    [Property]
    public bool Where_True_IsIdentity(bool some, int value) =>
        Make(some, value).Where(_ => true).Equals(Make(some, value));

    [Property]
    public bool Where_False_IsNone(bool some, int value) =>
        Make(some, value).Where(_ => false).IsNone;
}
