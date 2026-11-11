using System.Collections.Immutable;

namespace Derail;

/// <summary>A value that passed validation.</summary>
public readonly record struct Valid<T>(T Value);

/// <summary>One or more validation errors, in the order they were found.</summary>
public readonly struct Invalid<TError> : IEquatable<Invalid<TError>>
{
    private readonly ImmutableArray<TError> _errors;

    /// <summary>Creates an invalid case with at least one error.</summary>
    public Invalid(TError error, params ReadOnlySpan<TError> more) =>
        _errors = [error, .. more];

    private Invalid(ImmutableArray<TError> errors) => _errors = errors;

    /// <summary>The errors. Empty only for <c>default</c>.</summary>
    public IReadOnlyList<TError> Errors => _errors.IsDefault ? [] : _errors;

    public void Deconstruct(out IReadOnlyList<TError> errors) => errors = Errors;

    internal Invalid<TError> Concat(Invalid<TError> other) => new([.. Errors, .. other.Errors]);

    internal Invalid<TNewError> Select<TNewError>(Func<TError, TNewError> map) =>
        new([.. Errors.Select(map)]);

    public bool Equals(Invalid<TError> other) => Errors.SequenceEqual(other.Errors);

    public override bool Equals(object? obj) => obj is Invalid<TError> other && Equals(other);

    public override int GetHashCode() =>
        Errors.Aggregate(new HashCode(), (hash, error) => { hash.Add(error); return hash; }).ToHashCode();

    public static bool operator ==(Invalid<TError> left, Invalid<TError> right) => left.Equals(right);

    public static bool operator !=(Invalid<TError> left, Invalid<TError> right) => !left.Equals(right);
}

/// <summary>
/// The outcome of independent checks: either <see cref="Valid{T}"/> or <see cref="Invalid{TError}"/>.
/// Unlike <see cref="Result{T, TError}"/>, combining validations collects every error instead of stopping at the first.
/// </summary>
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
    public static Validation<IReadOnlyList<T>, TError> Sequence<T, TError>(IEnumerable<Validation<T, TError>> validations) =>
        validations.Aggregate(
            Valid<ImmutableList<T>, TError>([]),
            (acc, next) => acc.Zip(next, (values, value) => values.Add(value)))
            .Map(IReadOnlyList<T> (values) => values);
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
        public TResult Match<TResult>(Func<T, TResult> valid, Func<IReadOnlyList<TError>, TResult> invalid) =>
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
                (Invalid<TError> a, _) => a,
                (_, Invalid<TError> b) => b,
            };

        /// <summary>Combines three validations, collecting the errors of all.</summary>
        public Validation<TResult, TError> Zip<T2, T3, TResult>(
            Validation<T2, TError> second,
            Validation<T3, TError> third,
            Func<T, T2, T3, TResult> combine) =>
            validation
                .Zip(second, (a, b) => (a, b))
                .Zip(third, (ab, c) => combine(ab.a, ab.b, c));

        /// <summary>Returns the value, or <paramref name="fallback"/> when invalid.</summary>
        public T GetValueOrDefault(T fallback) => validation.Match(v => v, _ => fallback);

        /// <summary>Converts to a result whose error is the list of all errors.</summary>
        public Result<T, IReadOnlyList<TError>> ToResult() =>
            validation.Match(Result.Ok<T, IReadOnlyList<TError>>, Result.Error<T, IReadOnlyList<TError>>);
    }

    extension<T, TResult, TError>(Validation<Func<T, TResult>, TError> function)
    {
        /// <summary>Applies a wrapped function to a wrapped value, collecting the errors of both.</summary>
        public Validation<TResult, TError> Apply(Validation<T, TError> argument) =>
            function.Zip(argument, (f, x) => f(x));
    }
}
