namespace Derail;

/// <summary>A successful outcome.</summary>
public readonly record struct Ok<T>(T Value);

/// <summary>A failed outcome.</summary>
public readonly record struct Error<TError>(TError Value);

/// <summary>The outcome of an operation: either <see cref="Ok{T}"/> or <see cref="Error{TError}"/>.</summary>
public union Result<T, TError>(Ok<T>, Error<TError>);

/// <summary>Factory methods for <see cref="Result{T, TError}"/>.</summary>
public static class Result
{
    /// <summary>Creates a successful result.</summary>
    public static Result<T, TError> Ok<T, TError>(T value) => new Ok<T>(value);

    /// <summary>Creates a failed result.</summary>
    public static Result<T, TError> Error<T, TError>(TError error) => new Error<TError>(error);
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

        /// <summary>Returns the success value, or <paramref name="fallback"/> on failure.</summary>
        public T GetValueOrDefault(T fallback) =>
            result switch
            {
                Ok<T> o => o.Value,
                Error<TError> => fallback,
            };

        /// <summary>Discards the error, keeping the success value as an option.</summary>
        public Option<T> ToOption() => result.Match(Option.Some, _ => Option.None);

        /// <summary>LINQ support: <c>select</c>.</summary>
        public Result<TResult, TError> Select<TResult>(Func<T, TResult> map) => result.Map(map);

        /// <summary>LINQ support: multiple <c>from</c> clauses.</summary>
        public Result<TResult, TError> SelectMany<TNext, TResult>(
            Func<T, Result<TNext, TError>> bind,
            Func<T, TNext, TResult> project) =>
            result.Bind(v => bind(v).Map(n => project(v, n)));

        /// <summary>Combines two results; returns the first error without evaluating further.</summary>
        public Result<TResult, TError> Zip<TOther, TResult>(
            Result<TOther, TError> other,
            Func<T, TOther, TResult> combine) =>
            result.Bind(a => other.Map(b => combine(a, b)));

        /// <summary>Converts to a validation with a single error.</summary>
        public Validation<T, TError> ToValidation() =>
            result.Match(Validation.Valid<T, TError>, e => Validation.Invalid<T, TError>(e));
    }

    extension<T, TResult, TError>(Result<Func<T, TResult>, TError> function)
    {
        /// <summary>Applies a wrapped function to a wrapped value; returns the first error.</summary>
        public Result<TResult, TError> Apply(Result<T, TError> argument) =>
            function.Zip(argument, (f, x) => f(x));
    }
}
