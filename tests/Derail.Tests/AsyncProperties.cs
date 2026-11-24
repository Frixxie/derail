using FsCheck.Xunit;

namespace Derail.Tests;

public class AsyncProperties
{
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
