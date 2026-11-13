using AwesomeAssertions;
using FsCheck;
using FsCheck.Xunit;
using Xunit;

namespace Derail.Tests;

public class NonEmptyTests
{
    [Fact]
    public void Singleton_HasAnEmptyTail()
    {
        var values = new NonEmpty<int>(42);

        values.Head.Should().Be(42);
        values.Tail.Should().BeEmpty();
        values.Count.Should().Be(1);
        values[0].Should().Be(42);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    [InlineData(int.MinValue)]
    public void Indexer_RejectsOutOfRangeIndexes(int index)
    {
        var values = new NonEmpty<int>(1, 2);

        var act = () => values[index];

        act.Should().Throw<IndexOutOfRangeException>();
    }

    [Fact]
    public void FromEnumerable_EmptyIsNone() =>
        NonEmpty.FromEnumerable(Array.Empty<int>()).IsNone.Should().BeTrue();

    [Fact]
    public void Construction_SnapshotsTheTail()
    {
        var tail = new[] { 2, 3 };
        var values = new NonEmpty<int>(1, tail);
        tail[0] = 99;

        values.Head.Should().Be(1);
        values.Tail.Should().Equal(2, 3);
        values.Count.Should().Be(3);
        values[2].Should().Be(3);
        values.Should().Equal(1, 2, 3);
    }

    [Fact]
    public void FromEnumerable_EnumeratesOnce()
    {
        var enumerations = 0;
        IEnumerable<int> Source()
        {
            enumerations++;
            if (enumerations > 1) throw new InvalidOperationException("Enumerated twice");
            yield return 1;
            yield return 2;
        }

        NonEmpty.FromEnumerable(Source()).Match(values => values.ToArray(), () => []).Should().Equal(1, 2);
        enumerations.Should().Be(1);
    }

    [Property]
    public bool FromEnumerable_RoundTrips(NonEmptyArray<int> source) =>
        NonEmpty.FromEnumerable(source.Get).Match(values => values.SequenceEqual(source.Get), () => false);

    [Property]
    public bool Map_PreservesCountAndOrder(int head, int[] tail)
    {
        var values = new NonEmpty<int>(head, tail);
        var mapped = values.Map(x => x + 1);
        return mapped.Count == values.Count && mapped.SequenceEqual(values.Select(x => x + 1));
    }

    [Property]
    public bool Map_Composition(int head, int[] tail)
    {
        var values = new NonEmpty<int>(head, tail);
        return values.Map(x => x + 1).Map(x => x * 2)
            .SequenceEqual(values.Map(x => (x + 1) * 2));
    }

    [Property]
    public bool Append_PreservesBothInputs(int first, int[] leftTail, int second, int[] rightTail)
    {
        var left = new NonEmpty<int>(first, leftTail);
        var right = new NonEmpty<int>(second, rightTail);
        return left.Append(right).SequenceEqual(left.Concat(right))
            && left.SequenceEqual(new[] { first }.Concat(leftTail))
            && right.SequenceEqual(new[] { second }.Concat(rightTail));
    }
}
