using System.Collections.Immutable;

namespace Derail;

/// <summary>A value paired with immutable, ordered events. Composition never emits events.</summary>
/// <remarks>Composition is eager. Callers decide how and when to deliver the collected events.</remarks>
public sealed class Writer<T, TEvent>
{
    // Bind joins event trees in constant time; Events flattens once and caches the result.
    // Concurrent first reads may each flatten the immutable tree before one cached leaf is observed.
    private EventTree<TEvent> _events;

    /// <summary>Pairs a value with a snapshot of its events in encounter order.</summary>
    public Writer(T value, IEnumerable<TEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        Value = value;
        _events = new Leaf<TEvent>(events.ToImmutableArray());
    }

    private Writer(T value, EventTree<TEvent> events)
    {
        Value = value;
        _events = events;
    }

    /// <summary>Gets the computed value without reading the event sequence.</summary>
    public T Value { get; }

    /// <summary>Gets the ordered event snapshot, materializing deferred concatenations on first access.</summary>
    public IReadOnlyList<TEvent> Events => Flatten();

    /// <summary>Returns the value and its ordered events, as if reading both properties.</summary>
    public void Deconstruct(out T value, out IReadOnlyList<TEvent> events) =>
        (value, events) = (Value, Events);

    /// <summary>Transforms the value without changing the events.</summary>
    public Writer<TResult, TEvent> Map<TResult>(Func<T, TResult> map) => new(map(Value), _events);

    /// <summary>Transforms each event while preserving the value and event order.</summary>
    public Writer<T, TNewEvent> MapEvents<TNewEvent>(Func<TEvent, TNewEvent> map) =>
        new(Value, new Leaf<TNewEvent>(ImmutableArray.CreateRange(Flatten(), map)));

    /// <summary>Chains a computation, appending its events after this computation's events.</summary>
    public Writer<TResult, TEvent> Bind<TResult>(Func<T, Writer<TResult, TEvent>> bind)
    {
        var next = bind(Value);
        var events = (_events.Count, next._events.Count) switch
        {
            (0, _) => next._events,
            (_, 0) => _events,
            _ => new Concat<TEvent>(_events, next._events),
        };
        return new(next.Value, events);
    }

    private ImmutableArray<TEvent> Flatten()
    {
        if (_events is Leaf<TEvent> leaf) return leaf.Events;

        var builder = ImmutableArray.CreateBuilder<TEvent>(_events.Count);
        var pending = new Stack<EventTree<TEvent>>();
        pending.Push(_events);
        while (pending.TryPop(out var tree))
        {
            switch (tree)
            {
                case Leaf<TEvent> l:
                    builder.AddRange(l.Events);
                    break;
                case Concat<TEvent> c:
                    pending.Push(c.Right);
                    pending.Push(c.Left);
                    break;
            }
        }

        var flattened = builder.MoveToImmutable();
        _events = new Leaf<TEvent>(flattened);
        return flattened;
    }

    /// <summary>Combines values and appends events left-to-right.</summary>
    public Writer<TResult, TEvent> Zip<TOther, TResult>(Writer<TOther, TEvent> other, Func<T, TOther, TResult> combine) =>
        Bind(left => other.Map(right => combine(left, right)));

    /// <summary>LINQ support: <c>select</c>.</summary>
    public Writer<TResult, TEvent> Select<TResult>(Func<T, TResult> map) => Map(map);

    /// <summary>LINQ support: multiple <c>from</c> clauses.</summary>
    public Writer<TResult, TEvent> SelectMany<TNext, TResult>(
        Func<T, Writer<TNext, TEvent>> bind, Func<T, TNext, TResult> project) =>
        Bind(value => bind(value).Map(next => project(value, next)));
}

internal abstract record EventTree<TEvent>(int Count);

internal sealed record Leaf<TEvent>(ImmutableArray<TEvent> Events) : EventTree<TEvent>(Events.Length);

internal sealed record Concat<TEvent>(EventTree<TEvent> Left, EventTree<TEvent> Right)
    : EventTree<TEvent>(checked(Left.Count + Right.Count));

/// <summary>Factories and combinators for values with deferred events.</summary>
public static class Writer
{
    extension<T, TEvent>(Writer<Writer<T, TEvent>, TEvent> nested)
    {
        /// <summary>Removes one writer layer, keeping the inner value and appending inner events after outer events.</summary>
        public Writer<T, TEvent> Flatten() => nested.Bind(static inner => inner);
    }

    /// <summary>Creates a value without events.</summary>
    public static Writer<T, TEvent> Pure<T, TEvent>(T value) => new(value, []);

    /// <summary>Records one event without a meaningful result value.</summary>
    public static Writer<ValueTuple, TEvent> Tell<TEvent>(TEvent @event) => new(default, [@event]);
}
