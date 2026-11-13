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

    [Fact]
    public void FromEnumerable_SnapshotsMutableInput()
    {
        var source = new[] { 1, 2, 3 };
        var result = NonEmpty.FromEnumerable(source);
        source[0] = 99;
        source[1] = 99;

        result.Match(values => values.ToArray(), () => []).Should().Equal(1, 2, 3);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FromEnumerable_DisposesTheEnumerator(bool hasValue)
    {
        var disposed = false;
        IEnumerable<int> Source()
        {
            try
            {
                if (hasValue) yield return 42;
            }
            finally { disposed = true; }
        }

        var result = NonEmpty.FromEnumerable(Source());

        disposed.Should().BeTrue();
        result.IsSome.Should().Be(hasValue);
    }

    [Fact]
    public void Map_VisitsEveryElementExactlyOnceInOrder()
    {
        var visited = new List<int>();
        var values = new NonEmpty<int>(1, 2, 3);

        var mapped = values.Map(x => { visited.Add(x); return x * 2; });

        visited.Should().Equal(1, 2, 3);
        mapped.Should().Equal(2, 4, 6);
        values.Should().Equal(1, 2, 3);
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
