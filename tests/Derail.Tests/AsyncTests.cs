using AwesomeAssertions;
using Derail;
using Xunit;

namespace Derail.Tests;

public class ResultAsyncTests
{
    private static Result<int, string> Parse(string input) =>
        int.TryParse(input, out var value)
            ? Result.Ok<int, string>(value)
            : Result.Error<int, string>($"'{input}' is not a number");

    private static Task<Result<int, string>> ParseAsync(string input) => Task.FromResult(Parse(input));

    private static Task<Result<int, string>> PositiveAsync(int x) =>
        Task.FromResult(x > 0 ? Result.Ok<int, string>(x) : Result.Error<int, string>($"{x} is not positive"));

    private static Result<int, string> Positive(int x) =>
        x > 0 ? Result.Ok<int, string>(x) : Result.Error<int, string>($"{x} is not positive");

    public class OnResult
    {
        [Fact]
        public async Task MapAsync_TransformsOkValue() =>
            (await Parse("2").MapAsync(x => Task.FromResult(x * 2))).Should().Be(Result.Ok<int, string>(4));

        [Fact]
        public async Task MapAsync_DoesNotCallFunction_WhenError()
        {
            var called = false;

            var result = await Parse("x").MapAsync(x => { called = true; return Task.FromResult(x); });

            result.IsError.Should().BeTrue();
            called.Should().BeFalse();
        }

        [Fact]
        public async Task BindAsync_ChainsWhenOk() =>
            (await Parse("-1").BindAsync(PositiveAsync)).Should().Be(Result.Error<int, string>("-1 is not positive"));

        [Fact]
        public async Task BindAsync_DoesNotCallFunction_WhenError()
        {
            var called = false;

            var result = await Parse("x").BindAsync(x => { called = true; return PositiveAsync(x); });

            result.Should().Be(Result.Error<int, string>("'x' is not a number"));
            called.Should().BeFalse();
        }
    }

    public class OnTaskOfResult
    {
        [Fact]
        public async Task Map_TransformsOkValue() =>
            (await ParseAsync("2").Map(x => x + 1)).Should().Be(Result.Ok<int, string>(3));

        [Fact]
        public async Task MapAsync_TransformsOkValue() =>
            (await ParseAsync("2").MapAsync(x => Task.FromResult(x + 1))).Should().Be(Result.Ok<int, string>(3));

        [Fact]
        public async Task MapError_TransformsError() =>
            (await ParseAsync("x").MapError(e => e.Length)).Should().Be(Result.Error<int, int>("'x' is not a number".Length));

        [Fact]
        public async Task Bind_ChainsSyncFunction() =>
            (await ParseAsync("5").Bind(Positive)).Should().Be(Result.Ok<int, string>(5));

        [Fact]
        public async Task BindAsync_ChainsAsyncFunction() =>
            (await ParseAsync("0").BindAsync(PositiveAsync)).Should().Be(Result.Error<int, string>("0 is not positive"));

        [Fact]
        public async Task Match_FoldsOutcome()
        {
            (await ParseAsync("1").Match(v => $"ok {v}", e => e)).Should().Be("ok 1");
            (await ParseAsync("x").Match(v => $"ok {v}", e => e)).Should().Be("'x' is not a number");
        }

        [Theory]
        [InlineData("3")]
        [InlineData("-3")]
        [InlineData("x")]
        public async Task Pipeline_MatchesSyncEquivalent(string input)
        {
            var expected = Parse(input).Bind(Positive).Map(x => x * 2);

            var actual = await ParseAsync(input).BindAsync(PositiveAsync).Map(x => x * 2);

            actual.Should().Be(expected);
        }
    }
}

public class OptionAsyncTests
{
    private static readonly Option<int> NoInt = Option.None;

    private static Task<Option<int>> FindAsync(int key) =>
        Task.FromResult(key > 0 ? Option.Some(key * 10) : NoInt);

    private static Option<int> HalfIfEven(int x) => x % 2 == 0 ? Option.Some(x / 2) : Option.None;

    public class OnOption
    {
        [Fact]
        public async Task MapAsync_TransformsValue() =>
            (await Option.Some(2).MapAsync(x => Task.FromResult(x + 1))).Should().Be(Option.Some(3));

        [Fact]
        public async Task MapAsync_KeepsNone() =>
            (await NoInt.MapAsync(x => Task.FromResult(x + 1))).IsNone.Should().BeTrue();

        [Fact]
        public async Task BindAsync_ChainsWhenPresent() =>
            (await Option.Some(1).BindAsync(FindAsync)).Should().Be(Option.Some(10));

        [Fact]
        public async Task BindAsync_DoesNotCallFunction_WhenNone()
        {
            var called = false;

            var result = await NoInt.BindAsync(x => { called = true; return FindAsync(x); });

            result.IsNone.Should().BeTrue();
            called.Should().BeFalse();
        }
    }

    public class OnTaskOfOption
    {
        [Fact]
        public async Task Map_TransformsValue() =>
            (await FindAsync(1).Map(x => x + 1)).Should().Be(Option.Some(11));

        [Fact]
        public async Task MapAsync_TransformsValue() =>
            (await FindAsync(1).MapAsync(x => Task.FromResult(x + 1))).Should().Be(Option.Some(11));

        [Fact]
        public async Task Bind_ChainsSyncFunction() =>
            (await FindAsync(1).Bind(HalfIfEven)).Should().Be(Option.Some(5));

        [Fact]
        public async Task BindAsync_ChainsAsyncFunction() =>
            (await FindAsync(1).BindAsync(FindAsync)).Should().Be(Option.Some(100));

        [Fact]
        public async Task Match_FoldsOutcome()
        {
            (await FindAsync(1).Match(v => v, () => -1)).Should().Be(10);
            (await FindAsync(0).Match(v => v, () => -1)).Should().Be(-1);
        }

        [Fact]
        public async Task ToResult_UsesErrorWhenNone() =>
            (await FindAsync(0).ToResult(() => "not found")).Should().Be(Result.Error<int, string>("not found"));

        [Fact]
        public async Task ToResult_KeepsValueWhenPresent() =>
            (await FindAsync(2).ToResult(() => "not found")).Should().Be(Result.Ok<int, string>(20));
    }
}
