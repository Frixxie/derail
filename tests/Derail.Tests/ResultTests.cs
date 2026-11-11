using AwesomeAssertions;
using Derail;
using Xunit;

namespace Derail.Tests;

public class ResultTests
{
    private static Result<int, string> Parse(string input) =>
        int.TryParse(input, out var value)
            ? Result.Ok<int, string>(value)
            : Result.Error<int, string>($"'{input}' is not a number");

    private static readonly Result<int, string> Failed = Result.Error<int, string>("boom");

    public class Construction
    {
        [Fact]
        public void Ok_WrapsValue() =>
            (Result.Ok<int, string>(1) is Ok<int>(1)).Should().BeTrue();

        [Fact]
        public void Error_WrapsError() =>
            (Result.Error<int, string>("e") is Error<string>("e")).Should().BeTrue();

        [Fact]
        public void Cases_ConvertImplicitlyToResult()
        {
            Result<int, string> ok = new Ok<int>(1);
            Result<int, string> error = new Error<string>("e");

            ok.IsOk.Should().BeTrue();
            error.IsError.Should().BeTrue();
        }
    }

    public class Equality
    {
        [Fact]
        public void OkWithEqualValues_AreEqual() =>
            Result.Ok<int, string>(1).Should().Be(Result.Ok<int, string>(1));

        [Fact]
        public void ErrorWithEqualValues_AreEqual() =>
            Result.Error<int, string>("e").Should().Be(Result.Error<int, string>("e"));

        [Fact]
        public void OkAndError_AreNotEqual() =>
            Result.Ok<string, string>("x").Should().NotBe(Result.Error<string, string>("x"));
    }

    public class State
    {
        [Fact]
        public void IsOk_And_IsError_ForOk()
        {
            var result = Parse("1");

            result.IsOk.Should().BeTrue();
            result.IsError.Should().BeFalse();
        }

        [Fact]
        public void IsOk_And_IsError_ForError()
        {
            Failed.IsOk.Should().BeFalse();
            Failed.IsError.Should().BeTrue();
        }

        [Fact]
        public void IsOk_And_IsError_ForDefault()
        {
            Result<int, string> result = default;

            result.IsOk.Should().BeFalse();
            result.IsError.Should().BeFalse();
        }

        [Fact]
        public void PatternMatching_ExtractsValueOrError()
        {
            static string Describe(Result<int, string> r) => r switch
            {
                Ok<int>(var v) => $"ok {v}",
                Error<string>(var e) => $"error {e}",
            };

            Describe(Parse("1")).Should().Be("ok 1");
            Describe(Parse("x")).Should().Be("error 'x' is not a number");
        }
    }

    public class Match
    {
        [Fact]
        public void CallsOkBranch_WhenSuccessful() =>
            Parse("2").Match(v => v * 10, _ => -1).Should().Be(20);

        [Fact]
        public void CallsErrorBranch_WhenFailed() =>
            Failed.Match(_ => "ok", e => e).Should().Be("boom");
    }

    public class Map
    {
        [Fact]
        public void TransformsOkValue() =>
            Parse("2").Map(x => x * 2).Should().Be(Result.Ok<int, string>(4));

        [Fact]
        public void CanChangeType() =>
            Parse("2").Map(x => x.ToString()).Should().Be(Result.Ok<string, string>("2"));

        [Fact]
        public void KeepsError() =>
            Failed.Map(x => x * 2).Should().Be(Failed);

        [Fact]
        public void Identity_IsNoOp()
        {
            Parse("3").Map(x => x).Should().Be(Parse("3"));
            Failed.Map(x => x).Should().Be(Failed);
        }
    }

    public class MapError
    {
        [Fact]
        public void TransformsError() =>
            Failed.MapError(e => e.Length).Should().Be(Result.Error<int, int>(4));

        [Fact]
        public void KeepsOk() =>
            Parse("1").MapError(e => e.Length).Should().Be(Result.Ok<int, int>(1));

        [Fact]
        public void MapperIsNotCalled_WhenOk()
        {
            var called = false;

            Parse("1").MapError(e => { called = true; return e; });

            called.Should().BeFalse();
        }
    }

    public class Bind
    {
        private static Result<int, string> Positive(int x) =>
            x > 0 ? Result.Ok<int, string>(x) : Result.Error<int, string>($"{x} is not positive");

        [Fact]
        public void ChainsWhenOk() =>
            Parse("5").Bind(Positive).Should().Be(Result.Ok<int, string>(5));

        [Fact]
        public void ReturnsError_FromFunction() =>
            Parse("-1").Bind(Positive).Should().Be(Result.Error<int, string>("-1 is not positive"));

        [Fact]
        public void ShortCircuits_OnFirstError()
        {
            var called = false;

            Failed.Bind(x => { called = true; return Positive(x); }).Should().Be(Failed);

            called.Should().BeFalse();
        }

        [Fact]
        public void LeftIdentity() =>
            Result.Ok<int, string>(4).Bind(Positive).Should().Be(Positive(4));

        [Fact]
        public void RightIdentity() =>
            Parse("4").Bind(Result.Ok<int, string>).Should().Be(Parse("4"));
    }

    public class GetValueOrDefault
    {
        [Fact]
        public void ReturnsValue_WhenOk() =>
            Parse("1").GetValueOrDefault(0).Should().Be(1);

        [Fact]
        public void ReturnsFallback_WhenError() =>
            Failed.GetValueOrDefault(99).Should().Be(99);
    }

    public class ToOption
    {
        [Fact]
        public void OkBecomesSome() =>
            Parse("1").ToOption().Should().Be(Option.Some(1));

        [Fact]
        public void ErrorBecomesNone() =>
            Failed.ToOption().IsNone.Should().BeTrue();

        [Fact]
        public void RoundTrip_ThroughOption_KeepsOkValue() =>
            Parse("1").ToOption().ToResult(() => "lost").Should().Be(Parse("1"));
    }

    public class Select
    {
        [Fact]
        public void BehavesLikeMap()
        {
            Parse("2").Select(x => x + 1).Should().Be(Result.Ok<int, string>(3));
            Failed.Select(x => x + 1).Should().Be(Failed);
        }
    }

    public class SelectMany
    {
        [Fact]
        public void CombinesSuccesses() =>
            Parse("1")
                .SelectMany(_ => Parse("2"), (a, b) => a + b)
                .Should().Be(Result.Ok<int, string>(3));

        [Fact]
        public void ReturnsFirstError() =>
            Parse("x")
                .SelectMany(_ => Parse("y"), (a, b) => a + b)
                .Should().Be(Result.Error<int, string>("'x' is not a number"));

        [Fact]
        public void ReturnsErrorFromNext() =>
            Parse("1")
                .SelectMany(_ => Parse("y"), (a, b) => a + b)
                .Should().Be(Result.Error<int, string>("'y' is not a number"));
    }

    public class Combining
    {
        [Fact]
        public void NestedBind_CanUseEarlierValues()
        {
            var sum = Parse("1").Bind(a => Parse("2").Map(b => a + b));

            sum.Should().Be(Result.Ok<int, string>(3));
        }
    }

    public class QuerySyntax
    {
        [Fact]
        public void Select_MapsValue()
        {
            var result = from x in Parse("2") select x + 1;

            result.Should().Be(Result.Ok<int, string>(3));
        }

        [Fact]
        public void From_CombinesSuccesses()
        {
            var result = from a in Parse("1")
                         from b in Parse("2")
                         select a + b;

            result.Should().Be(Result.Ok<int, string>(3));
        }

        [Fact]
        public void From_ReturnsFirstError()
        {
            var result = from a in Parse("x")
                         from b in Parse("y")
                         select a + b;

            result.Should().Be(Result.Error<int, string>("'x' is not a number"));
        }

        [Fact]
        public void EqualsMethodSyntax()
        {
            var query = from a in Parse("1")
                        from b in Parse("2")
                        select a + b;

            query.Should().Be(Parse("1").Bind(a => Parse("2").Map(b => a + b)));
        }
    }

    public class Zip
    {
        [Fact]
        public void CombinesSuccesses() =>
            Parse("1").Zip(Parse("2"), (a, b) => a + b).Should().Be(Result.Ok<int, string>(3));

        [Fact]
        public void ReturnsFirstError_WithoutAccumulating() =>
            Parse("x").Zip(Parse("y"), (a, b) => a + b)
                .Should().Be(Result.Error<int, string>("'x' is not a number"));

        [Fact]
        public void ReturnsErrorFromOther() =>
            Parse("1").Zip(Parse("y"), (a, b) => a + b)
                .Should().Be(Result.Error<int, string>("'y' is not a number"));
    }

    public class Apply
    {
        [Fact]
        public void AppliesOkFunction_ToOkValue() =>
            Result.Ok<Func<int, int>, string>(x => x + 1).Apply(Parse("1")).Should().Be(Result.Ok<int, string>(2));

        [Fact]
        public void ReturnsFunctionError_First() =>
            Result.Error<Func<int, int>, string>("no function").Apply(Parse("x"))
                .Should().Be(Result.Error<int, string>("no function"));

        [Theory]
        [InlineData("1")]
        [InlineData("x")]
        public void Identity(string input) =>
            Result.Ok<Func<int, int>, string>(x => x).Apply(Parse(input)).Should().Be(Parse(input));
    }

    public class ToValidation
    {
        [Fact]
        public void OkBecomesValid() =>
            Parse("1").ToValidation().Should().Be(Validation.Valid<int, string>(1));

        [Fact]
        public void ErrorBecomesInvalid() =>
            Failed.ToValidation().Should().Be(Validation.Invalid<int, string>("boom"));
    }
}
