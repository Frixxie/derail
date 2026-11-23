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
    public bool Map_Composition(int value, int[] events, Func<int, int> f, Func<int, int> g)
    {
        var writer = new Writer<int, int>(value, events);
        var left = writer.Map(f).Map(g);
        var right = writer.Map(x => g(f(x)));
        return left.Value == right.Value && left.Events.SequenceEqual(right.Events);
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

    [Fact]
    public void Zip_RejectsEventCountOverflow()
    {
        var writer = Writer.Tell(1);
        for (var i = 0; i < 30; i++)
            writer = writer.Zip(writer, static (value, _) => value);

        Assert.Throws<OverflowException>(() => writer.Zip(writer, static (value, _) => value));
    }

    [Fact]
    public async Task Concurrent_first_reads_return_complete_ordered_snapshots()
    {
        const int count = 4096;
        var writer = Writer.Pure<int, int>(0);
        for (var i = 0; i < count; i++) writer = writer.Bind(static x => new Writer<int, int>(x + 1, [x]));

        using var start = new Barrier(4);
        var token = TestContext.Current.CancellationToken;
        var readers = Enumerable.Range(0, start.ParticipantCount).Select(_ => Task.Factory.StartNew(() =>
        {
            start.SignalAndWait(token);
            return writer.Events;
        }, token, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();

        var snapshots = await Task.WhenAll(readers);
        foreach (var events in snapshots.Append(writer.Events)) Assert.Equal(Enumerable.Range(0, count), events);
    }

    [Fact]
    public void Bind_LongLeftNestedChain_KeepsEventOrder()
    {
        const int count = 100_000;
        var writer = Writer.Pure<int, int>(0);
        for (var i = 0; i < count; i++) writer = writer.Bind(static x => new Writer<int, int>(x + 1, [x]));

        writer.Value.Should().Be(count);
        writer.Events.Should().Equal(Enumerable.Range(0, count));
    }

    [Fact]
    public void Bind_LongRightNestedChain_KeepsEventOrder()
    {
        const int count = 100_000;
        var writer = new Writer<int, int>(count, [count - 1]);
        for (var i = count - 2; i >= 0; i--)
        {
            var rest = writer;
            writer = new Writer<int, int>(i, [i]).Bind(_ => rest);
        }

        writer.Events.Should().Equal(Enumerable.Range(0, count));
    }

    [Property]
    public bool Bind_Chain_EventsAreConcatenationOfSegments(int[][] segments)
    {
        var writer = segments.Aggregate(
            Writer.Pure<int, int>(0),
            (acc, segment) => acc.Bind(x => new Writer<int, int>(x + 1, segment)));
        return writer.Value == segments.Length
            && writer.Events.SequenceEqual(segments.SelectMany(s => s))
            && writer.Events.SequenceEqual(writer.Events);
    }

    [Property]
    public bool Zip_CombinesValuesAndOrderedEvents(int left, int right, int[] first, int[] second)
    {
        var writer = new Writer<int, int>(left, first).Zip(new Writer<int, int>(right, second), (a, b) => (a, b));
        return writer.Value == (left, right) && writer.Events.SequenceEqual(first.Concat(second));
    }
}
