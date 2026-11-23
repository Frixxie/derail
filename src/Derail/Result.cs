using System.Collections.Immutable;

namespace Derail;

/// <summary>A successful outcome.</summary>
public readonly record struct Ok<T>(T Value);

/// <summary>A failed outcome.</summary>
public readonly record struct Error<TError>(TError Value);

/// <summary>The outcome of an operation: either <see cref="Ok{T}"/> or <see cref="Error{TError}"/>.</summary>
/// <remarks>The default value matches neither case. Both case predicates return false;
/// combinators that inspect an uninitialized case throw <see cref="System.Runtime.CompilerServices.SwitchExpressionException"/>.</remarks>
public union Result<T, TError>(Ok<T>, Error<TError>);

/// <summary>Factory methods for <see cref="Result{T, TError}"/>.</summary>
public static class Result
{
    /// <summary>Creates a successful result.</summary>
    public static Result<T, TError> Ok<T, TError>(T value) => new Ok<T>(value);

    /// <summary>Creates a failed result.</summary>
    public static Result<T, TError> Error<T, TError>(TError error) => new Error<TError>(error);

    /// <summary>Collects successes in order into an immutable snapshot, or stops at the first error.</summary>
    public static Result<IReadOnlyList<T>, TError> Sequence<T, TError>(IEnumerable<Result<T, TError>> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        var values = ImmutableArray.CreateBuilder<T>();
        foreach (var result in results)
        {
            switch (result)
            {
                case Ok<T>(var value): values.Add(value); break;
                case Error<TError> error: return error;
                default: throw new System.Runtime.CompilerServices.SwitchExpressionException(result);
            }
        }
        return Ok<IReadOnlyList<T>, TError>(values.DrainToImmutable());
    }

    /// <summary>Maps elements in order and collects their values, stopping at the first error.</summary>
    public static Result<IReadOnlyList<T>, TError> Traverse<TSource, T, TError>(IEnumerable<TSource> source, Func<TSource, Result<T, TError>> map) =>
        Sequence(source.Select(map));
}

/// <summary>Combinators for <see cref="Result{T, TError}"/>.</summary>
public static class ResultExtensions
{
    extension<T, TError>(Result<T, TError> result)
    {
        /// <summary>True when the result is successful.</summary>
        public bool IsOk => result is Ok<T>;

        /// <summary>True when the result is a failure.</summary>
        public bool IsError => result is Error<TError>;

        /// <summary>Folds the result into a single value.</summary>
        public TResult Match<TResult>(Func<T, TResult> ok, Func<TError, TResult> error) =>
            result switch
            {
                Ok<T> o => ok(o.Value),
                Error<TError> e => error(e.Value),
            };

        /// <summary>Transforms the success value.</summary>
        public Result<TResult, TError> Map<TResult>(Func<T, TResult> map) =>
            result switch
            {
                Ok<T> o => new Ok<TResult>(map(o.Value)),
                Error<TError> e => e,
            };

        /// <summary>Transforms the error value.</summary>
        public Result<T, TNewError> MapError<TNewError>(Func<TError, TNewError> map) =>
            result switch
            {
                Ok<T> o => o,
                Error<TError> e => new Error<TNewError>(map(e.Value)),
            };

        /// <summary>Chains a result-returning function when successful.</summary>
        public Result<TResult, TError> Bind<TResult>(Func<T, Result<TResult, TError>> bind) =>
            result.Match(bind, Result.Error<TResult, TError>);

        /// <summary>Runs a replacement computation on error, possibly changing the error type. Preserves success.</summary>
        public Result<T, TNewError> Recover<TNewError>(Func<TError, Result<T, TNewError>> recover) =>
            result.Match(Result.Ok<T, TNewError>, recover);

        /// <summary>Observes the error, if present, and returns the original result.</summary>
        public Result<T, TError> TapError(Action<TError> action)
        {
            switch (result)
            {
                case Ok<T>: return result;
                case Error<TError>(var error):
                    action(error);
                    return result;
                default: throw new System.Runtime.CompilerServices.SwitchExpressionException(result);
            }
        }

        /// <summary>Runs a synchronous action on success and returns the original result.</summary>
        public Result<T, TError> Tap(Action<T> action)
        {
            switch (result)
            {
                case Ok<T>(var value):
                    action(value);
                    return result;
                case Error<TError>:
                    return result;
                default:
                    throw new System.Runtime.CompilerServices.SwitchExpressionException(result);
            }
        }

        /// <summary>Preserves the result unless a success value fails the predicate, then creates an error from that value.</summary>
        public Result<T, TError> Ensure(Func<T, bool> predicate, Func<T, TError> errorFactory) =>
            result switch
            {
                Ok<T>(var value) => predicate(value) ? result : new Error<TError>(errorFactory(value)),
                Error<TError> => result,
            };

        /// <summary>Returns the success value, or <paramref name="fallback"/> on failure.</summary>
        public T GetValueOrDefault(T fallback) =>
            result switch
            {
                Ok<T> o => o.Value,
                Error<TError> => fallback,
            };

        /// <summary>Returns the success value or invokes <paramref name="fallback"/> once with the original error.</summary>
        public T GetValueOrElse(Func<TError, T> fallback) => result.Match(static value => value, fallback);

        /// <summary>Discards the error, keeping the success value as an option.</summary>
        public Option<T> ToOption() => result.Match(Option.Some, _ => Option.None);

        /// <summary>LINQ support: <c>select</c>.</summary>
        public Result<TResult, TError> Select<TResult>(Func<T, TResult> map) => result.Map(map);

        /// <summary>LINQ support: multiple <c>from</c> clauses.</summary>
        public Result<TResult, TError> SelectMany<TNext, TResult>(
            Func<T, Result<TNext, TError>> bind,
            Func<T, TNext, TResult> project) =>
            result.Bind(v => bind(v).Map(n => project(v, n)));

        /// <summary>Combines two already-evaluated results. Returns the first error and calls <paramref name="combine"/> only when both succeed.</summary>
        public Result<TResult, TError> Zip<TOther, TResult>(
            Result<TOther, TError> other,
            Func<T, TOther, TResult> combine) =>
            result.Bind(a => other.Map(b => combine(a, b)));

        /// <summary>Converts to a validation with a single error.</summary>
        public Validation<T, TError> ToValidation() =>
            result.Match(Validation.Valid<T, TError>, e => Validation.Invalid<T, TError>(e));
    }

    extension<T, TError>(Result<Result<T, TError>, TError> nested)
    {
        /// <summary>Removes one result layer, returning the outer error or the inner result.</summary>
        public Result<T, TError> Flatten() => nested.Bind(static inner => inner);
    }

    extension<T, TResult, TError>(Result<Func<T, TResult>, TError> function)
    {
        /// <summary>Applies a wrapped function to a wrapped value; returns the first error.</summary>
        public Result<TResult, TError> Apply(Result<T, TError> argument) =>
            function.Zip(argument, (f, x) => f(x));
    }
}
