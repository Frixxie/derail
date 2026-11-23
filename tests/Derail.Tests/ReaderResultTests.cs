using AwesomeAssertions;
using FsCheck.Xunit;
using Xunit;

namespace Derail.Tests;

public class ReaderResultTests
{
    [Property]
    public bool FromResult_preserves_the_case_and_payload_in_every_environment(bool ok, int? payload, int env)
    {
        var source = ok ? Result.Ok<int?, int?>(payload) : Result.Error<int?, int?>(payload);
        var reader = ReaderResult.FromResult<int, int?, int?>(source);
        return source.Equals(reader.Run(env)) && source.Equals(reader.Run(~env));
    }

    [Fact]
    public void FromLookup_reads_each_environment_at_run_and_creates_errors_only_for_absence()
    {
        var reads = new List<bool>();
        var errors = 0;
        Func<bool, Option<string?>> lookup = present =>
        {
            reads.Add(present);
            return present ? Option.Some<string?>(null) : Option.None;
        };
        var reader = ReaderResult.FromLookup(lookup, () => ++errors);

        Assert.Empty(reads);
        Assert.Equal(0, errors);
        Assert.Equal(Result.Ok<string?, int>(null), reader.Run(true));
        Assert.Equal(Result.Error<string?, int>(1), reader.Run(false));
        Assert.Equal(Result.Error<string?, int>(2), reader.Run(false));
        Assert.Equal([true, false, false], reads);
    }

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
    public bool Map_Identity(Func<int, int?> lookup, int environment)
    {
        var reader = ReaderResult.FromLookup((int env) => Option.FromNullable(lookup(env)), () => "missing");
        return reader.Map(x => x).Run(environment).Equals(reader.Run(environment));
    }

    [Property]
    public bool Map_Composition(Func<int, int?> lookup, int environment, Func<int, int> f, Func<int, int> g)
    {
        var reader = ReaderResult.FromLookup((int env) => Option.FromNullable(lookup(env)), () => "missing");
        return reader.Map(f).Map(g).Run(environment).Equals(reader.Map(x => g(f(x))).Run(environment));
    }

    [Property]
    public bool Bind_LeftIdentity(int value, int environment, Func<int, int?> lookup)
    {
        ReaderResult<int, int, string> Next(int x) => new(env => Option.FromNullable(lookup(env + x)).ToResult(() => "missing"));
        return ReaderResult.Pure<int, int, string>(value).Bind(Next).Run(environment).Equals(Next(value).Run(environment));
    }

    [Property]
    public bool Bind_RightIdentity(bool ok, int value, int environment)
    {
        var result = ok ? Result.Ok<int, string>(value) : Result.Error<int, string>("failed");
        var reader = ReaderResult.FromResult<int, int, string>(result);
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
        var reader = ReaderResult.FromResult<int, int, string>(result);

        return reader.Bind(Add).Bind(Half).Run(environment)
            .Equals(reader.Bind(x => Add(x).Bind(Half)).Run(environment));
    }
}
