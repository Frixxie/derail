using System.Collections.Immutable;

namespace Derail;

/// <summary>A value that passed validation.</summary>
/// <param name="Value">The validated payload.</param>
public readonly record struct Valid<T>(T Value);

/// <summary>One or more validation errors, in the order they were found.</summary>
public sealed class Invalid<TError> : IEquatable<Invalid<TError>>
{
    /// <summary>Creates an invalid case with at least one error.</summary>
    public Invalid(TError error, params ReadOnlySpan<TError> more) =>
        Errors = new(error, more);

    internal Invalid(NonEmpty<TError> errors) => Errors = errors ?? throw new ArgumentNullException(nameof(errors));

    /// <summary>The errors, always containing at least one element.</summary>
    public NonEmpty<TError> Errors { get; }

    /// <summary>Deconstructs this failure into its non-empty, ordered errors.</summary>
    public void Deconstruct(out NonEmpty<TError> errors) => errors = Errors;

    internal Invalid<TError> Concat(Invalid<TError> other) => new(Errors.Append(other.Errors));

    internal Invalid<TNewError> Select<TNewError>(Func<TError, TNewError> map) =>
        new(Errors.Map(map));

    /// <summary>Tests whether the other failure contains the same ordered errors.</summary>
    public bool Equals(Invalid<TError>? other) => other is not null && Errors.Equals(other.Errors);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Invalid<TError> other && Equals(other);

    /// <summary>Returns the hash of the ordered errors.</summary>
    public override int GetHashCode() => Errors.GetHashCode();

    /// <summary>Compares ordered errors; two null references are equal.</summary>
    public static bool operator ==(Invalid<TError>? left, Invalid<TError>? right) =>
        ReferenceEquals(left, right) || left is not null && left.Equals(right);

    /// <summary>Returns the negation of the ordered equality comparison.</summary>
    public static bool operator !=(Invalid<TError>? left, Invalid<TError>? right) => !(left == right);
}

/// <summary>
/// The outcome of independent checks: either <see cref="Valid{T}"/> or <see cref="Invalid{TError}"/>.
/// Unlike <see cref="Result{T, TError}"/>, combining validations collects every error instead of stopping at the first.
/// </summary>
/// <remarks>The default value matches neither case. Both case predicates return false;
/// combinators that inspect an uninitialized case throw <see cref="System.Runtime.CompilerServices.SwitchExpressionException"/>.</remarks>
public union Validation<T, TError>(Valid<T>, Invalid<TError>);

/// <summary>Factory methods for <see cref="Validation{T, TError}"/>.</summary>
public static class Validation
{
    /// <summary>Creates a valid value.</summary>
    public static Validation<T, TError> Valid<T, TError>(T value) => new Valid<T>(value);

    /// <summary>Creates an invalid value with at least one error.</summary>
    public static Validation<T, TError> Invalid<T, TError>(TError error, params ReadOnlySpan<TError> more) =>
        new Invalid<TError>(error, more);

    /// <summary>Turns many validations into one: all values when every item is valid, otherwise every error.</summary>
    public static Validation<IReadOnlyList<T>, TError> Sequence<T, TError>(IEnumerable<Validation<T, TError>> validations)
    {
        ArgumentNullException.ThrowIfNull(validations);
        var values = ImmutableArray.CreateBuilder<T>();
        ImmutableArray<TError>.Builder? errors = null;

        foreach (var validation in validations)
        {
            switch (validation)
            {
                case Valid<T> valid:
                    if (errors is null) values.Add(valid.Value);
                    break;
                case Invalid<TError> invalid:
                    errors ??= ImmutableArray.CreateBuilder<TError>();
                    invalid.Errors.CopyTo(errors);
                    break;
                default:
                    throw new System.Runtime.CompilerServices.SwitchExpressionException(validation);
            }
        }

        if (errors is null) return new Valid<IReadOnlyList<T>>(values.DrainToImmutable());

        // Every Invalid contributes at least one error, so the first element always exists.
        var allErrors = errors.DrainToImmutable();
        return new Invalid<TError>(allErrors[0], allErrors.AsSpan()[1..]);
    }

    /// <summary>Maps every element in order. Collects all values on success, otherwise all errors in encounter order.</summary>
    public static Validation<IReadOnlyList<T>, TError> Traverse<TSource, T, TError>(IEnumerable<TSource> source, Func<TSource, Validation<T, TError>> map) =>
        Sequence(source.Select(map));

    /// <summary>Maps every element in order. Collects all values into a non-empty collection on success, otherwise all errors in encounter order.</summary>
    public static Validation<NonEmpty<T>, TError> Traverse<TSource, T, TError>(NonEmpty<TSource> source, Func<TSource, Validation<T, TError>> map)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(map);
        var mapped = source.Map(map);
        return mapped.Head.Zip(Sequence(mapped.Tail), static (head, tail) => new NonEmpty<T>(head, ImmutableArray.CreateRange(tail)));
    }
}

/// <summary>Combinators for <see cref="Validation{T, TError}"/>.</summary>
public static class ValidationExtensions
{
    extension<T, TError>(Validation<T, TError> validation)
    {
        /// <summary>True when the value is valid.</summary>
        public bool IsValid => validation is Valid<T>;

        /// <summary>True when there is at least one error.</summary>
        public bool IsInvalid => validation is Invalid<TError>;

        /// <summary>Folds the validation into a single value.</summary>
        public TResult Match<TResult>(Func<T, TResult> valid, Func<NonEmpty<TError>, TResult> invalid) =>
            validation switch
            {
                Valid<T> v => valid(v.Value),
                Invalid<TError> i => invalid(i.Errors),
            };

        /// <summary>Transforms the valid value.</summary>
        public Validation<TResult, TError> Map<TResult>(Func<T, TResult> map) =>
            validation switch
            {
                Valid<T> v => new Valid<TResult>(map(v.Value)),
                Invalid<TError> i => i,
            };

        /// <summary>Transforms every error.</summary>
        public Validation<T, TNewError> MapErrors<TNewError>(Func<TError, TNewError> map) =>
            validation switch
            {
                Valid<T> v => v,
                Invalid<TError> i => i.Select(map),
            };

        /// <summary>Combines two validations, collecting the errors of both.</summary>
        public Validation<TResult, TError> Zip<TOther, TResult>(
            Validation<TOther, TError> other,
            Func<T, TOther, TResult> combine) =>
            (validation, other) switch
            {
                (Valid<T> a, Valid<TOther> b) => new Valid<TResult>(combine(a.Value, b.Value)),
                (Invalid<TError> a, Invalid<TError> b) => a.Concat(b),
                (Invalid<TError> a, Valid<TOther>) => a,
                (Valid<T>, Invalid<TError> b) => b,
            };

        /// <summary>
        /// Chains a validation that depends on the valid value. Unlike <c>Zip</c>, this short-circuits:
        /// when invalid, <paramref name="next"/> is not invoked and its errors are not collected.
        /// Use <c>Zip</c> for independent checks and this only when a check needs an earlier value.
        /// </summary>
        public Validation<TResult, TError> AndThen<TResult>(Func<T, Validation<TResult, TError>> next) =>
            validation switch
            {
                Valid<T> v => next(v.Value),
                Invalid<TError> i => i,
            };

        /// <summary>Returns the value, or <paramref name="fallback"/> when invalid.</summary>
        public T GetValueOrDefault(T fallback) => validation.Match(v => v, _ => fallback);

        /// <summary>Returns the valid value or invokes <paramref name="fallback"/> once with all errors in encounter order.</summary>
        public T GetValueOrElse(Func<NonEmpty<TError>, T> fallback) => validation.Match(static value => value, fallback);

        /// <summary>Converts to a result whose error is the non-empty collection of all errors.</summary>
        public Result<T, NonEmpty<TError>> ToResult() =>
            validation.Match(Result.Ok<T, NonEmpty<TError>>, Result.Error<T, NonEmpty<TError>>);
    }

    extension<T, TResult, TError>(Validation<Func<T, TResult>, TError> function)
    {
        /// <summary>Applies a wrapped function to a wrapped value, collecting the errors of both.</summary>
        public Validation<TResult, TError> Apply(Validation<T, TError> argument) =>
            function.Zip(argument, (f, x) => f(x));
    }

}
