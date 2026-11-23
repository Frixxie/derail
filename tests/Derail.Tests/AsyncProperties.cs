using FsCheck.Xunit;

namespace Derail.Tests;

public class AsyncProperties
{
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
        Option<int> source = present ? Option.Some(value) : default(Option<int>);
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
