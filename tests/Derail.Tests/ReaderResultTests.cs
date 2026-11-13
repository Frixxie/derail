using AwesomeAssertions;
using FsCheck.Xunit;
using Xunit;

namespace Derail.Tests;

public class ReaderResultTests
{
    [Fact]
    public void BindAndZip_DoNotRunContinuationAfterError()
    {
        var calls = 0;
        var failed = new ReaderResult<int, int, string>(_ => Result.Error<int, string>("failed"));
        var next = new ReaderResult<int, int, string>(env => { calls++; return Result.Ok<int, string>(env); });
        var bound = failed.Bind(_ => next);
        var zipped = failed.Zip(next, (a, b) => a + b);

        calls.Should().Be(0);
        bound.Run(1).Should().Be(Result.Error<int, string>("failed"));
        zipped.Run(1).Should().Be(Result.Error<int, string>("failed"));
        calls.Should().Be(0);
    }

    [Fact]
    public void Linq_CombinesValuesUsingTheSameEnvironment()
    {
        var reader =
            from first in ReaderResult.FromReader<int, int, string>(Reader.Ask<int>())
            from second in new ReaderResult<int, int, string>(env => Result.Ok<int, string>(env + 1))
            select first + second;

        reader.Local<string>(text => text.Length).Run("test").Should().Be(Result.Ok<int, string>(9));
    }

    [Fact]
    public void MapError_TransformsFailure() =>
        new ReaderResult<int, int, string>(_ => Result.Error<int, string>("missing"))
            .Map(x => x + 1).MapError(e => e.Length).Run(0)
            .Should().Be(Result.Error<int, int>(7));

    [Fact]
    public void Recover_UsesTheSameEnvironment()
    {
        var reader = new ReaderResult<int, int, string>(_ => Result.Error<int, string>("missing"))
            .Recover(error => new ReaderResult<int, int, int>(env => Result.Ok<int, int>(env + error.Length)));

        reader.Run(3).Should().Be(Result.Ok<int, int>(10));
    }

    [Fact]
    public void Recover_DoesNotRunOnSuccess()
    {
        var reader = ReaderResult.Pure<int, int, string>(42)
            .Recover<int>(_ => throw new InvalidOperationException("Unexpected recovery"));

        reader.Run(0).Should().Be(Result.Ok<int, int>(42));
    }

    [Fact]
    public void Zip_PropagatesSecondErrorAfterRunningLeft()
    {
        var order = new List<string>();
        var left = new ReaderResult<int, int, string>(env =>
        {
            order.Add("left");
            return Result.Ok<int, string>(env);
        });
        var right = new ReaderResult<int, int, string>(_ =>
        {
            order.Add("right");
            return Result.Error<int, string>("second");
        });

        left.Zip(right, (a, b) => a + b).Run(2).Should().Be(Result.Error<int, string>("second"));
        order.Should().Equal("left", "right");
    }

    [Fact]
    public void ToReader_PreservesLazinessAndRerunsTheComputation()
    {
        var calls = 0;
        var reader = new ReaderResult<int, int, string>(env =>
        {
            calls++;
            return Result.Ok<int, string>(env);
        }).Map(x => x + 1).Local<string>(text => text.Length).ToReader();

        calls.Should().Be(0);
        reader.Run("a").Should().Be(Result.Ok<int, string>(2));
        reader.Run("abc").Should().Be(Result.Ok<int, string>(4));
        calls.Should().Be(2);
    }

    [Property]
    public bool Bind_RightIdentity(bool ok, int value, int environment)
    {
        var result = ok ? Result.Ok<int, string>(value) : Result.Error<int, string>("failed");
        var reader = new ReaderResult<int, int, string>(_ => result);
        return reader.Bind(ReaderResult.Pure<int, int, string>).Run(environment).Equals(result);
    }

    [Property]
    public bool Bind_Associativity(bool ok, int value, int environment)
    {
        static ReaderResult<int, int, string> Add(int x) => new(env => Result.Ok<int, string>(env + x));
        static ReaderResult<int, int, string> Half(int x) => new(_ => x % 2 == 0
            ? Result.Ok<int, string>(x / 2)
            : Result.Error<int, string>("odd"));
        var result = ok ? Result.Ok<int, string>(value) : Result.Error<int, string>("failed");
        var reader = new ReaderResult<int, int, string>(_ => result);

        return reader.Bind(Add).Bind(Half).Run(environment)
            .Equals(reader.Bind(x => Add(x).Bind(Half)).Run(environment));
    }
}
