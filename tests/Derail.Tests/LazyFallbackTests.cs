using System.Runtime.CompilerServices;
using Xunit;

namespace Derail.Tests;

public class LazyFallbackTests
{
    [Theory]
    [InlineData("value")]
    [InlineData(null)]
    public void Successful_values_skip_the_fallback(string? value)
    {
        Assert.Same(value, Option.Some(value).GetValueOrElse(() => throw new InvalidOperationException()));
        Assert.Same(value, Result.Ok<string?, int>(value).GetValueOrElse(_ => throw new InvalidOperationException()));
        Assert.Same(value, Validation.Valid<string?, int>(value).GetValueOrElse(_ => throw new InvalidOperationException()));
    }

    [Fact]
    public void None_invokes_the_fallback_once()
    {
        var calls = 0;
        Option<int> missing = Option.None;
        Assert.Equal(1, missing.GetValueOrElse(() => ++calls));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Result_passes_the_original_error_once()
    {
        var error = new object();
        var observed = new List<object>();
        var value = Result.Error<int, object>(error).GetValueOrElse(e => { observed.Add(e); return 42; });
        Assert.Equal(42, value);
        Assert.Same(error, Assert.Single(observed));
    }

    [Fact]
    public void Validation_passes_all_accumulated_errors_once_in_order()
    {
        var invalid = Validation.Invalid<string, string>("first", "second")
            .Zip(Validation.Invalid<string, string>("third"), (a, b) => a + b);
        var calls = 0;
        var value = invalid.GetValueOrElse(errors => { calls++; return string.Join(", ", errors); });
        Assert.Equal("first, second, third", value);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Uninitialized_outcomes_do_not_invoke_a_fallback()
    {
        Assert.Throws<SwitchExpressionException>(() => default(Option<int>).GetValueOrElse(() => throw new InvalidOperationException()));
        Assert.Throws<SwitchExpressionException>(() => default(Result<int, int>).GetValueOrElse(_ => throw new InvalidOperationException()));
        Assert.Throws<SwitchExpressionException>(() => default(Validation<int, int>).GetValueOrElse(_ => throw new InvalidOperationException()));
    }
}
