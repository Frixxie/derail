namespace Derail;

/// <summary>A lazy, reusable environment-dependent computation that stops at the first error.</summary>
public sealed class ReaderResult<TEnv, T, TError>
{
    private readonly Reader<TEnv, Result<T, TError>> _reader;

    /// <summary>Wraps a fallible computation without running it.</summary>
    public ReaderResult(Func<TEnv, Result<T, TError>> run) => _reader = new(run);

    public Result<T, TError> Run(TEnv environment) => _reader.Run(environment);

    /// <summary>Exposes the underlying computation without running it.</summary>
    public Reader<TEnv, Result<T, TError>> ToReader() => _reader;

    /// <summary>Lazily transforms successful values.</summary>
    public ReaderResult<TEnv, TResult, TError> Map<TResult>(Func<T, TResult> map) =>
        new(env => Run(env).Map(map));

    /// <summary>Lazily transforms errors.</summary>
    public ReaderResult<TEnv, T, TNewError> MapError<TNewError>(Func<TError, TNewError> map) =>
        new(env => Run(env).MapError(map));

    /// <summary>Chains successful computations using the same environment.</summary>
    public ReaderResult<TEnv, TResult, TError> Bind<TResult>(Func<T, ReaderResult<TEnv, TResult, TError>> bind) =>
        new(env => Run(env).Bind(value => bind(value).Run(env)));

    /// <summary>Recovers from an error using the same environment.</summary>
    public ReaderResult<TEnv, T, TNewError> Recover<TNewError>(Func<TError, ReaderResult<TEnv, T, TNewError>> recover) =>
        new(env => Run(env).Match(Result.Ok<T, TNewError>, error => recover(error).Run(env)));

    /// <summary>Runs left-to-right, skipping the second computation when the first fails.</summary>
    public ReaderResult<TEnv, TResult, TError> Zip<TOther, TResult>(
        ReaderResult<TEnv, TOther, TError> other, Func<T, TOther, TResult> combine) =>
        Bind(left => other.Map(right => combine(left, right)));

    /// <summary>Adapts an outer environment to the environment this computation needs.</summary>
    public ReaderResult<TOuter, T, TError> Local<TOuter>(Func<TOuter, TEnv> project) =>
        new(env => Run(project(env)));

    /// <summary>LINQ support: <c>select</c>.</summary>
    public ReaderResult<TEnv, TResult, TError> Select<TResult>(Func<T, TResult> map) => Map(map);

    /// <summary>LINQ support: multiple <c>from</c> clauses.</summary>
    public ReaderResult<TEnv, TResult, TError> SelectMany<TNext, TResult>(
        Func<T, ReaderResult<TEnv, TNext, TError>> bind, Func<T, TNext, TResult> project) =>
        Bind(value => bind(value).Map(next => project(value, next)));
}

/// <summary>Factory methods for fallible environment-dependent computations.</summary>
public static class ReaderResult
{
    /// <summary>Creates a computation that always succeeds with the supplied value.</summary>
    public static ReaderResult<TEnv, T, TError> Pure<TEnv, T, TError>(T value) =>
        new(_ => Result.Ok<T, TError>(value));

    /// <summary>Lifts an infallible reader into a successful result computation.</summary>
    public static ReaderResult<TEnv, T, TError> FromReader<TEnv, T, TError>(Reader<TEnv, T> reader) =>
        new(env => Result.Ok<T, TError>(reader.Run(env)));
}
