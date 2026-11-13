using System.Collections;
using System.Collections.Immutable;

namespace Derail;

/// <summary>An immutable ordered collection with a required first element.</summary>
/// <remarks>A reference type so that default initialization cannot create an empty instance.</remarks>
public sealed class NonEmpty<T> : IReadOnlyList<T>
{
    private readonly ImmutableArray<T> _tail;

    /// <summary>Creates a collection from its first element and a snapshot of its tail.</summary>
    public NonEmpty(T head, params ReadOnlySpan<T> tail)
    {
        Head = head;
        _tail = [.. tail];
    }

    /// <summary>The first element.</summary>
    public T Head { get; }

    /// <summary>The remaining elements, which may be empty.</summary>
    public IReadOnlyList<T> Tail => _tail;

    /// <summary>The number of elements, always at least one.</summary>
    public int Count => 1 + _tail.Length;

    public T this[int index] => index == 0 ? Head : _tail[index - 1];

    /// <summary>Transforms every element while preserving order and non-emptiness.</summary>
    public NonEmpty<TResult> Map<TResult>(Func<T, TResult> map) =>
        new(map(Head), [.. _tail.Select(map)]);

    /// <summary>Concatenates two collections without changing either input.</summary>
    public NonEmpty<T> Append(NonEmpty<T> other) =>
        new(Head, [.. _tail, other.Head, .. other._tail]);

    public IEnumerator<T> GetEnumerator()
    {
        yield return Head;
        foreach (var value in _tail) yield return value;
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>Factory methods for non-empty collections.</summary>
public static class NonEmpty
{
    /// <summary>Snapshots a sequence once, returning none when it is empty.</summary>
    public static Option<NonEmpty<T>> FromEnumerable<T>(IEnumerable<T> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var items = values.ToImmutableArray();
        return items.IsEmpty ? Option.None : Option.Some(new NonEmpty<T>(items[0], items.AsSpan()[1..]));
    }
}
