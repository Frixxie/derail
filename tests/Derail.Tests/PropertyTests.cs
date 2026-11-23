using FsCheck;
using FsCheck.Xunit;
using Derail;

namespace Derail.Tests;

public class ResultProperties
{
    private static Result<T, string> Pure<T>(T value) => Result.Ok<T, string>(value);

    [Property]
    public bool Apply_Identity(bool ok, int value, string error)
    {
        var source = Make(ok, value, error);
        return Pure<Func<int, int>>(x => x).Apply(source).Equals(source);
    }

    [Property]
    public bool Apply_Homomorphism(Func<int, int> f, int x) =>
        Pure(f).Apply(Pure(x)).Equals(Pure(f(x)));

    [Property]
    public bool Apply_Interchange(bool ok, Func<int, int> f, int x)
    {
        var u = ok ? Pure(f) : Result.Error<Func<int, int>, string>("u");
        return u.Apply(Pure(x)).Equals(Pure<Func<Func<int, int>, int>>(g => g(x)).Apply(u));
    }

    [Property]
    public bool Apply_Composition(bool firstOk, bool secondOk, bool valueOk, Func<int, int> f, Func<int, int> g, int x)
    {
        var u = firstOk ? Pure(f) : Result.Error<Func<int, int>, string>("first");
        var v = secondOk ? Pure(g) : Result.Error<Func<int, int>, string>("second");
        var w = Make(valueOk, x, "value");
        Func<Func<int, int>, Func<Func<int, int>, Func<int, int>>> compose = a => b => n => a(b(n));
        return Pure(compose).Apply(u).Apply(v).Apply(w).Equals(u.Apply(v.Apply(w)));
    }

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
    [Property]
    public bool Apply_Identity(bool some, int x)
    {
        var source = Make(some, x);
        return Option.Some<Func<int, int>>(n => n).Apply(source).Equals(source);
    }

    [Property]
    public bool Apply_Homomorphism(Func<int, int> f, int x) =>
        Option.Some(f).Apply(Option.Some(x)).Equals(Option.Some(f(x)));

    [Property]
    public bool Apply_Interchange(bool some, Func<int, int> f, int x)
    {
        Option<Func<int, int>> u = some ? Option.Some(f) : Option.None;
        return u.Apply(Option.Some(x)).Equals(Option.Some<Func<Func<int, int>, int>>(g => g(x)).Apply(u));
    }

    [Property]
    public bool Apply_Composition(bool firstSome, bool secondSome, bool valueSome, Func<int, int> f, Func<int, int> g, int x)
    {
        Option<Func<int, int>> u = firstSome ? Option.Some(f) : Option.None;
        Option<Func<int, int>> v = secondSome ? Option.Some(g) : Option.None;
        var w = Make(valueSome, x);
        Func<Func<int, int>, Func<Func<int, int>, Func<int, int>>> compose = a => b => n => a(b(n));
        return Option.Some(compose).Apply(u).Apply(v).Apply(w).Equals(u.Apply(v.Apply(w)));
    }

    [Property]
    public bool Map_Composition(bool some, int x, Func<int, int> f, Func<int, int> g)
    {
        var source = Make(some, x);
        return source.Map(f).Map(g).Equals(source.Map(n => g(f(n))));
    }

    [Property]
    public bool Bind_LeftIdentity(int x, Func<int, int?> f)
    {
        Option<int> Next(int n) => Option.FromNullable(f(n));
        return Option.Some(x).Bind(Next).Equals(Next(x));
    }

    [Property]
    public bool Bind_RightIdentity(bool present, int? x)
    {
        Option<int?> source = present ? Option.Some(x) : Option.None;
        return source.Bind(Option.Some).Equals(source);
    }

    [Property]
    public bool Bind_Associativity(bool some, int x, Func<int, int?> f, Func<int, int?> g)
    {
        var source = Make(some, x);
        Option<int> First(int n) => Option.FromNullable(f(n));
        Option<int> Second(int n) => Option.FromNullable(g(n));
        return source.Bind(First).Bind(Second).Equals(source.Bind(n => First(n).Bind(Second)));
    }

    [Property]
    public bool Some_RoundTripsNullablePayload_AndIsDistinctFromNone(int? payload)
    {
        Option<int?> some = new Some<int?>(payload);
        return some is Some<int?>(var value) && value == payload
            && !some.Equals(default(Option<int?>));
    }

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

public class ValidationProperties
{
    private static Validation<T, string> Make<T>(bool ok, T value, NonEmptyArray<string> errors) =>
        ok ? Validation.Valid<T, string>(value) : Validation.Invalid<T, string>(errors.Get[0], errors.Get[1..]);

    private static Validation<int, string> Make(bool ok, int value, NonEmptyArray<string> errors) =>
        Make<int>(ok, value, errors);

    private static Validation<Func<int, int>, string> MakeFn(bool ok, int k, NonEmptyArray<string> errors) =>
        Make<Func<int, int>>(ok, x => x + k, errors);

    private static IReadOnlyList<string> Errors<T>(Validation<T, string> v) => v.Match(IReadOnlyList<string> (_) => [], e => e);

    [Property]
    public bool Map_Identity(bool ok, int value, NonEmptyArray<string> errors) =>
        Make(ok, value, errors).Map(x => x).Equals(Make(ok, value, errors));

    [Property]
    public bool Map_Composition(bool ok, int value, NonEmptyArray<string> errors) =>
        Make(ok, value, errors).Map(x => x + 1).Map(x => x * 3)
            .Equals(Make(ok, value, errors).Map(x => (x + 1) * 3));

    [Property]
    public bool Apply_Identity(bool ok, int value, NonEmptyArray<string> errors) =>
        Validation.Valid<Func<int, int>, string>(x => x).Apply(Make(ok, value, errors))
            .Equals(Make(ok, value, errors));

    [Property]
    public bool Apply_Homomorphism(int k, int value) =>
        Validation.Valid<Func<int, int>, string>(x => x + k).Apply(Validation.Valid<int, string>(value))
            .Equals(Validation.Valid<int, string>(value + k));

    [Property]
    public bool Apply_Interchange(bool ok, int k, NonEmptyArray<string> errors, int y) =>
        MakeFn(ok, k, errors).Apply(Validation.Valid<int, string>(y))
            .Equals(Validation.Valid<Func<Func<int, int>, int>, string>(f => f(y)).Apply(MakeFn(ok, k, errors)));

    [Property]
    public bool Apply_Composition(
        bool uOk, int uK, NonEmptyArray<string> uErrors,
        bool vOk, int vK, NonEmptyArray<string> vErrors,
        bool wOk, int w, NonEmptyArray<string> wErrors)
    {
        var u = MakeFn(uOk, uK, uErrors);
        var v = MakeFn(vOk, vK, vErrors);
        var x = Make(wOk, w, wErrors);
        Func<Func<int, int>, Func<Func<int, int>, Func<int, int>>> compose = f => g => a => f(g(a));

        return Validation.Valid<Func<Func<int, int>, Func<Func<int, int>, Func<int, int>>>, string>(compose)
            .Apply(u).Apply(v).Apply(x)
            .Equals(u.Apply(v.Apply(x)));
    }

    [Property]
    public bool FourWayZip_PreservesOrderAndCombinesExactlyOnceOnSuccess(
        bool aOk, bool bOk, bool cOk, bool dOk, int a, int b, int c, int d)
    {
        Validation<int, string> Field(bool valid, int value, string name) => valid
            ? Validation.Valid<int, string>(value)
            : Validation.Invalid<int, string>(name, name + "!");
        var fields = new[] { Field(aOk, a, "a"), Field(bOk, b, "b"), Field(cOk, c, "c"), Field(dOk, d, "d") };
        var calls = 0;
        var actual = fields[0].Zip(fields[1], fields[2], fields[3], (w, x, y, z) => { calls++; return (w, x, y, z); });
        var errors = fields.SelectMany(Errors).ToArray();
        return actual.Match(values => errors.Length == 0 && values == (a, b, c, d) && calls == 1,
            observed => observed.SequenceEqual(errors) && calls == 0);
    }

    [Property]
    public bool Zip_LeftUnit(bool ok, int value, NonEmptyArray<string> errors) =>
        Validation.Valid<ValueTuple, string>(default).Zip(Make(ok, value, errors), (_, x) => x)
            .Equals(Make(ok, value, errors));

    [Property]
    public bool Zip_RightUnit(bool ok, int value, NonEmptyArray<string> errors) =>
        Make(ok, value, errors).Zip(Validation.Valid<ValueTuple, string>(default), (x, _) => x)
            .Equals(Make(ok, value, errors));

    [Property]
    public bool Zip_Associativity(
        bool aOk, int a, NonEmptyArray<string> aErrors,
        bool bOk, int b, NonEmptyArray<string> bErrors,
        bool cOk, int c, NonEmptyArray<string> cErrors)
    {
        var va = Make(aOk, a, aErrors);
        var vb = Make(bOk, b, bErrors);
        var vc = Make(cOk, c, cErrors);

        return va.Zip(vb, (x, y) => (x, y)).Zip(vc, (xy, z) => (xy.x, xy.y, z))
            .Equals(va.Zip(vb.Zip(vc, (y, z) => (y, z)), (x, yz) => (x, yz.y, yz.z)));
    }

    [Property]
    public bool Zip_ConcatenatesErrorsInOrder(
        bool aOk, int a, NonEmptyArray<string> aErrors,
        bool bOk, int b, NonEmptyArray<string> bErrors)
    {
        var va = Make(aOk, a, aErrors);
        var vb = Make(bOk, b, bErrors);
        return Errors(va.Zip(vb, (x, y) => x + y)).SequenceEqual([.. Errors(va), .. Errors(vb)]);
    }

    [Property]
    public bool Sequence_IsValidExactlyWhenAllAre_AndPreservesOrder((bool ok, int value, NonEmptyArray<string> errors)[] items)
    {
        var validations = items.Select(i => Make(i.ok, i.value, i.errors)).ToList();
        var sequenced = Validation.Sequence(validations);

        return sequenced.Match(
            values => validations.All(v => v.IsValid) && values.SequenceEqual(items.Select(i => i.value)),
            errors => errors.Count > 0 && validations.Any(v => v.IsInvalid) && errors.SequenceEqual(validations.SelectMany(Errors)));
    }

    [Property]
    public bool MapErrors_Identity(bool ok, int value, NonEmptyArray<string> errors) =>
        Make(ok, value, errors).MapErrors(e => e).Equals(Make(ok, value, errors));

    [Property]
    public bool MapErrors_Composition(bool ok, int value, NonEmptyArray<string> errors) =>
        Make(ok, value, errors).MapErrors(e => e + "!").MapErrors(e => e.Length)
            .Equals(Make(ok, value, errors).MapErrors(e => (e + "!").Length));

    [Property]
    public bool Invalid_PreservesHeadAndTail(NonEmptyArray<int> errors)
    {
        var invalid = new Invalid<int>(errors.Get[0], errors.Get.AsSpan()[1..]);

        return invalid.Errors.Head == errors.Get[0]
            && invalid.Errors.Count == errors.Get.Length
            && invalid.Errors.Tail.SequenceEqual(errors.Get.Skip(1));
    }

    [Property]
    public bool Invalid_WithSameOrderedErrors_IsInterchangeableAsAHashKey(int head, int[] tail)
    {
        var keys = new HashSet<Invalid<int>> { new(head, tail) };
        return keys.Contains(new Invalid<int>(head, tail))
            && !keys.Contains(new Invalid<int>(~head, tail));
    }

    [Property]
    public bool MapErrors_PreservesNonEmptinessCountAndOrder(NonEmptyArray<int> errors) =>
        Validation.Invalid<int, int>(errors.Get[0], errors.Get.AsSpan()[1..])
            .MapErrors(error => error.ToString())
            .Match(_ => false, mapped => mapped.Count > 0
                && mapped.Count == errors.Get.Length
                && mapped.SequenceEqual(errors.Get.Select(error => error.ToString())));

    [Property]
    public bool ToResult_PreservesValueAndErrors(bool ok, int value, NonEmptyArray<string> errors) =>
        Make(ok, value, errors).ToResult().Match(
            v => ok && v == value,
            e => !ok && e.Count > 0 && e.SequenceEqual(errors.Get));

    [Property]
    public bool Sequence_MatchesZipFold((bool invalid, int value, NonEmptyArray<int> errors)[] items)
    {
        var validations = items.Select(item => !item.invalid
            ? Validation.Valid<int, int>(item.value)
            : Validation.Invalid<int, int>(item.errors.Get[0], item.errors.Get.AsSpan()[1..])).ToArray();
        var expected = validations.Aggregate(
            Validation.Valid<IReadOnlyList<int>, int>([]),
            (acc, next) => acc.Zip(next, (values, value) => (IReadOnlyList<int>)[.. values, value]));
        var actual = Validation.Sequence(validations);

        return expected.Match(
            values => actual.Match(other => values.SequenceEqual(other), _ => false),
            errors => actual.Match(_ => false, other => errors.SequenceEqual(other)));
    }
}
