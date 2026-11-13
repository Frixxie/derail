using AwesomeAssertions;
using FsCheck.Xunit;
using Xunit;

namespace Derail.Tests;

public class WriterTests
{
    [Fact]
    public void Construction_SnapshotsEvents()
    {
        var events = new List<string> { "created" };
        var writer = new Writer<int, string>(42, events);
        events.Add("changed");

        var (value, recorded) = writer;
        value.Should().Be(42);
        recorded.Should().Equal("created");
    }

    [Fact]
    public void Linq_AccumulatesEventsInEncounterOrder()
    {
        var workflow =
            from first in new Writer<int, string>(2, ["first"])
            from second in new Writer<int, string>(3, ["second"])
            select first + second;

        workflow.Value.Should().Be(5);
        workflow.Events.Should().Equal("first", "second");
        workflow.MapEvents(e => e.Length).Events.Should().Equal(5, 6);
    }

    [Fact]
    public void Tell_RecordsOneEventWithoutAValue() =>
        Writer.Tell("created").Events.Should().Equal("created");

    [Property]
    public bool Map_Identity(int value, string[] events)
    {
        var writer = new Writer<int, string>(value, events).Map(x => x);
        return writer.Value == value && writer.Events.SequenceEqual(events);
    }

    [Property]
    public bool Bind_RightIdentity(int value, int[] events)
    {
        var writer = new Writer<int, int>(value, events).Bind(Writer.Pure<int, int>);
        return writer.Value == value && writer.Events.SequenceEqual(events);
    }

    [Property]
    public bool Bind_LeftIdentity(int value)
    {
        static Writer<int, int> Next(int x) => new(x + 1, [x]);
        var writer = Writer.Pure<int, int>(value).Bind(Next);
        var expected = Next(value);
        return writer.Value == expected.Value && writer.Events.SequenceEqual(expected.Events);
    }

    [Property]
    public bool MapEvents_PreservesValueAndEventOrder(int value, int[] events)
    {
        var writer = new Writer<int, int>(value, events).MapEvents(x => x + 1);
        return writer.Value == value && writer.Events.SequenceEqual(events.Select(x => x + 1));
    }

    [Property]
    public bool Bind_Associativity(int value, int[] events)
    {
        static Writer<int, int> Next(int x) => new(x + 1, [x]);
        static Writer<int, int> Double(int x) => new(x * 2, [x, x]);
        var writer = new Writer<int, int>(value, events);
        var left = writer.Bind(Next).Bind(Double);
        var right = writer.Bind(x => Next(x).Bind(Double));
        return left.Value == right.Value && left.Events.SequenceEqual(right.Events);
    }

    [Property]
    public bool Zip_CombinesValuesAndOrderedEvents(int left, int right, int[] first, int[] second)
    {
        var writer = new Writer<int, int>(left, first).Zip(new Writer<int, int>(right, second), (a, b) => (a, b));
        return writer.Value == (left, right) && writer.Events.SequenceEqual(first.Concat(second));
    }
}
