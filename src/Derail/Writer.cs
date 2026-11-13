using System.Collections.Immutable;

namespace Derail;

/// <summary>A value paired with immutable, ordered events. Composition never emits events.</summary>
/// <remarks>Composition is eager. Callers decide how and when to deliver the collected events.</remarks>
public sealed class Writer<T, TEvent>
{
    private readonly ImmutableArray<TEvent> _events;

    /// <summary>Pairs a value with a snapshot of its events in encounter order.</summary>
    public Writer(T value, IEnumerable<TEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        Value = value;
        _events = events.ToImmutableArray();
    }

    public T Value { get; }

    public IReadOnlyList<TEvent> Events => _events;

    public void Deconstruct(out T value, out IReadOnlyList<TEvent> events) =>
        (value, events) = (Value, Events);

    /// <summary>Transforms the value without changing the events.</summary>
    public Writer<TResult, TEvent> Map<TResult>(Func<T, TResult> map) => new(map(Value), _events);

    /// <summary>Transforms each event while preserving the value and event order.</summary>
    public Writer<T, TNewEvent> MapEvents<TNewEvent>(Func<TEvent, TNewEvent> map) =>
        new(Value, _events.Select(map));

    /// <summary>Chains a computation, appending its events after this computation's events.</summary>
    public Writer<TResult, TEvent> Bind<TResult>(Func<T, Writer<TResult, TEvent>> bind)
    {
        var next = bind(Value);
        return new(next.Value, _events.AddRange(next._events));
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

/// <summary>Factory methods for values with deferred events.</summary>
public static class Writer
{
    /// <summary>Creates a value without events.</summary>
    public static Writer<T, TEvent> Pure<T, TEvent>(T value) => new(value, []);

    /// <summary>Records one event without a meaningful result value.</summary>
    public static Writer<ValueTuple, TEvent> Tell<TEvent>(TEvent @event) => new(default, [@event]);
}
