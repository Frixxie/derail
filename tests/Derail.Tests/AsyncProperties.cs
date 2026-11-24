using FsCheck.Xunit;

namespace Derail.Tests;

public class AsyncProperties
{
    private static Func<int, Result<int, string>> Fallible(Func<int, int?> f, string step) =>
        x => Option.FromNullable(f(x)).ToResult(() => step);

    private static Result<int, string> MakeResult(bool ok, int value) =>
        ok ? Result.Ok<int, string>(value) : Result.Error<int, string>("upstream");

    private static Option<int> MakeOption(bool some, int value) =>
        some ? Option.Some(value) : Option.None;

    [Property]
    public async Task<bool> Result_BindAsync_LeftIdentity(int value, Func<int, int?> f)
    {
        var next = Fallible(f, "f");
        var task = await Result.Ok<int, string>(value).BindAsync(x => Task.FromResult(next(x)));
        var valueTask = await Result.Ok<int, string>(value).BindAsync(x => ValueTask.FromResult(next(x)));
        return task.Equals(next(value)) && valueTask.Equals(next(value));
    }

    [Property]
    public async Task<bool> Result_BindAsync_RightIdentity(bool ok, int value)
    {
        var source = MakeResult(ok, value);
        var task = await source.BindAsync(x => Task.FromResult(Result.Ok<int, string>(x)));
        var valueTask = await source.BindAsync(x => ValueTask.FromResult(Result.Ok<int, string>(x)));
        return task.Equals(source) && valueTask.Equals(source);
    }

    [Property]
    public async Task<bool> Result_BindAsync_Associativity(bool ok, int value, Func<int, int?> f, Func<int, int?> g)
    {
        var (first, second) = (Fallible(f, "f"), Fallible(g, "g"));
        var source = MakeResult(ok, value);
        var leftNested = await source.BindAsync(x => Task.FromResult(first(x))).BindAsync(x => Task.FromResult(second(x)));
        var rightNested = await source.BindAsync(x => Task.FromResult(first(x)).BindAsync(y => Task.FromResult(second(y))));
        var valueTask = await source.BindAsync(x => ValueTask.FromResult(first(x))).BindAsync(x => ValueTask.FromResult(second(x)));
        return leftNested.Equals(rightNested) && leftNested.Equals(source.Bind(first).Bind(second)) && valueTask.Equals(leftNested);
    }

    [Property]
    public async Task<bool> Option_BindAsync_LeftIdentity(int value, Func<int, int?> f)
    {
        Option<int> Next(int x) => Option.FromNullable(f(x));
        var task = await Option.Some(value).BindAsync(x => Task.FromResult(Next(x)));
        var valueTask = await Option.Some(value).BindAsync(x => ValueTask.FromResult(Next(x)));
        return task.Equals(Next(value)) && valueTask.Equals(Next(value));
    }

    [Property]
    public async Task<bool> Option_BindAsync_RightIdentity(bool some, int value)
    {
        var source = MakeOption(some, value);
        var task = await source.BindAsync(x => Task.FromResult(Option.Some(x)));
        var valueTask = await source.BindAsync(x => ValueTask.FromResult(Option.Some(x)));
        return task.Equals(source) && valueTask.Equals(source);
    }

    [Property]
    public async Task<bool> Option_BindAsync_Associativity(bool some, int value, Func<int, int?> f, Func<int, int?> g)
    {
        Option<int> First(int x) => Option.FromNullable(f(x));
        Option<int> Second(int x) => Option.FromNullable(g(x));
        var source = MakeOption(some, value);
        var leftNested = await source.BindAsync(x => Task.FromResult(First(x))).BindAsync(x => Task.FromResult(Second(x)));
        var rightNested = await source.BindAsync(x => Task.FromResult(First(x)).BindAsync(y => Task.FromResult(Second(y))));
        var valueTask = await source.BindAsync(x => ValueTask.FromResult(First(x))).BindAsync(x => ValueTask.FromResult(Second(x)));
        return leftNested.Equals(rightNested) && leftNested.Equals(source.Bind(First).Bind(Second)) && valueTask.Equals(leftNested);
    }

    [Property]
    public async Task<bool> Result_MapAndBind_AgreeAcrossSyncTaskAndValueTask(bool ok, int value, Func<int, int> map, Func<int, int?> lookup)
    {
        var source = ok ? Result.Ok<int, string>(value) : Result.Error<int, string>("upstream");
        Result<int, string> Next(int x) => Option.FromNullable(lookup(x)).ToResult(() => "missing");
        var expected = source.Map(map).Bind(Next);
        var task = await source.MapAsync(x => Task.FromResult(map(x))).BindAsync(x => Task.FromResult(Next(x)));
        var valueTask = await source.MapAsync(x => ValueTask.FromResult(map(x))).BindAsync(x => ValueTask.FromResult(Next(x)));
        return expected.Equals(task) && expected.Equals(valueTask);
    }

    [Property]
    public async Task<bool> Option_BindAsync_AgreesWithBind(bool present, int value, int minimum)
    {
        Option<int> source = present ? Option.Some(value) : Option.None;
        Option<int> Keep(int x) => x >= minimum ? Option.Some(x) : Option.None;

        var expected = source.Bind(Keep);
        var task = await Task.FromResult(source).BindAsync(x => Task.FromResult(Keep(x)));
        var valueTask = await ValueTask.FromResult(source).BindAsync(x => ValueTask.FromResult(Keep(x)));

        return task.Equals(expected) && valueTask.Equals(expected);
    }

    [Property]
    public async Task<bool> Result_EnsureAsync_AgreesWithEnsure(bool ok, int value, int minimum)
    {
        Result<int, string> source = ok ? new Ok<int>(value) : new Error<string>("upstream");
        bool MeetsMinimum(int x) => x >= minimum;
        string BelowMinimum(int x) => $"{x} < {minimum}";

        var expected = source.Ensure(MeetsMinimum, BelowMinimum);
        var task = await Task.FromResult(source).EnsureAsync(x => Task.FromResult(MeetsMinimum(x)), BelowMinimum);
        var valueTask = await ValueTask.FromResult(source).EnsureAsync(x => ValueTask.FromResult(MeetsMinimum(x)), BelowMinimum);

        return task.Equals(expected) && valueTask.Equals(expected);
    }
}
