using AwesomeAssertions;
using FsCheck.Xunit;
using Xunit;

namespace Derail.Tests;

public class ReaderTests
{
    [Fact]
    public void Composition_IsLazyAndRunsAgainWithEachEnvironment()
    {
        var calls = 0;
        var reader = new Reader<int, int>(env => { calls++; return env; }).Map(x => x + 1);

        calls.Should().Be(0);
        reader.Run(2).Should().Be(3);
        reader.Run(5).Should().Be(6);
        calls.Should().Be(2);
    }

    [Fact]
    public void Linq_SharesEnvironmentAcrossSteps()
    {
        var reader =
            from first in Reader.Ask<int>()
            from second in new Reader<int, int>(env => env * 2)
            select first + second;

        reader.Local<string>(text => text.Length).Run("test").Should().Be(12);
    }

    [Fact]
    public void Zip_RunsLeftThenRight()
    {
        var order = new List<string>();
        var left = new Reader<int, int>(env => { order.Add("left"); return env; });
        var right = new Reader<int, int>(env => { order.Add("right"); return env + 1; });

        left.Zip(right, (a, b) => a + b).Run(2).Should().Be(5);
        order.Should().Equal("left", "right");
    }

    [Property]
    public bool Map_Composition(int environment) =>
        Reader.Ask<int>().Map(x => x + 1).Map(x => x * 2).Run(environment)
        == Reader.Ask<int>().Map(x => (x + 1) * 2).Run(environment);

    [Property]
    public bool Bind_Associativity(int environment)
    {
        static Reader<int, int> Add(int x) => new(env => env + x);
        static Reader<int, int> Multiply(int x) => new(env => env * x);
        var reader = Reader.Ask<int>();
        return reader.Bind(Add).Bind(Multiply).Run(environment)
            == reader.Bind(x => Add(x).Bind(Multiply)).Run(environment);
    }

    [Property]
    public bool Pure_IgnoresEnvironment(int value, int environment) =>
        Reader.Pure<int, int>(value).Run(environment) == value;

    [Property]
    public bool Bind_RightIdentity(int environment) =>
        Reader.Ask<int>().Bind(Reader.Pure<int, int>).Run(environment) == environment;

    [Property]
    public bool Bind_LeftIdentity(int value, int environment)
    {
        static Reader<int, int> Add(int x) => new(env => env + x);
        return Reader.Pure<int, int>(value).Bind(Add).Run(environment) == Add(value).Run(environment);
    }
}
