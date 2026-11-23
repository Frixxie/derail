namespace Derail;

/// <summary>A reusable, asynchronous environment-dependent computation that stops at the first typed error.</summary>
/// <remarks>Constructing and composing workflows does not run them. Exceptions and cancellation are not domain errors.</remarks>
public sealed class AsyncReaderResult<TEnv, T, TError>
{
    private readonly Func<TEnv, CancellationToken, Task<Result<T, TError>>> _run;

    /// <summary>Wraps a computation without invoking it.</summary>
    public AsyncReaderResult(Func<TEnv, CancellationToken, Task<Result<T, TError>>> run)
    {
        ArgumentNullException.ThrowIfNull(run);
        _run = run;
    }

    /// <summary>Starts a fresh execution. An already-canceled token skips the computation.</summary>
    public async Task<Result<T, TError>> Run(TEnv environment, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await _run(environment, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Transforms the success value when run.</summary>
    public AsyncReaderResult<TEnv, TResult, TError> Map<TResult>(Func<T, TResult> map) =>
        new((env, token) => Run(env, token).Map(map));

    /// <summary>Transforms a typed error when run.</summary>
    public AsyncReaderResult<TEnv, T, TNewError> MapError<TNewError>(Func<TError, TNewError> map) =>
        new((env, token) => Run(env, token).MapError(map));

    /// <summary>Runs a dependent computation on success, using the same environment and token.</summary>
    public AsyncReaderResult<TEnv, TResult, TError> Bind<TResult>(Func<T, AsyncReaderResult<TEnv, TResult, TError>> bind) =>
        new((env, token) => Run(env, token).BindAsync(value => bind(value).Run(env, token)));

    /// <summary>Runs a replacement computation on typed error, using the same environment and token.</summary>
    public AsyncReaderResult<TEnv, T, TNewError> Recover<TNewError>(Func<TError, AsyncReaderResult<TEnv, T, TNewError>> recover) =>
        new((env, token) => Run(env, token).RecoverAsync(error => recover(error).Run(env, token)));

    /// <summary>Checks a successful value and creates an error only if it fails the predicate.</summary>
    public AsyncReaderResult<TEnv, T, TError> Ensure(Func<T, bool> predicate, Func<T, TError> errorFactory) =>
        new(async (env, token) => (await Run(env, token).ConfigureAwait(false)).Ensure(predicate, errorFactory));

    /// <summary>Runs left to right, skipping the second computation if the first fails.</summary>
    public AsyncReaderResult<TEnv, TResult, TError> Zip<TOther, TResult>(AsyncReaderResult<TEnv, TOther, TError> other, Func<T, TOther, TResult> combine) =>
        Bind(left => other.Map(right => combine(left, right)));

    /// <summary>Projects an outer environment when run, preserving the token.</summary>
    public AsyncReaderResult<TOuter, T, TError> Local<TOuter>(Func<TOuter, TEnv> project) =>
        new((outer, token) => Run(project(outer), token));

    /// <summary>LINQ support: projects a successful value.</summary>
    public AsyncReaderResult<TEnv, TResult, TError> Select<TResult>(Func<T, TResult> map) => Map(map);

    /// <summary>LINQ support: chains dependent computations.</summary>
    public AsyncReaderResult<TEnv, TResult, TError> SelectMany<TNext, TResult>(
        Func<T, AsyncReaderResult<TEnv, TNext, TError>> bind, Func<T, TNext, TResult> project) =>
        Bind(value => bind(value).Map(next => project(value, next)));
}

/// <summary>Factory methods for asynchronous, fallible environment-dependent computations.</summary>
public static class AsyncReaderResult
{
    /// <summary>Creates a computation that returns the supplied value.</summary>
    public static AsyncReaderResult<TEnv, T, TError> Pure<TEnv, T, TError>(T value) =>
        FromResult<TEnv, T, TError>(Result.Ok<T, TError>(value));

    /// <summary>Reads the environment as a successful value.</summary>
    public static AsyncReaderResult<TEnv, TEnv, TError> Ask<TEnv, TError>() =>
        new((env, _) => Task.FromResult(Result.Ok<TEnv, TError>(env)));

    /// <summary>Lifts an already computed result without changing its case.</summary>
    public static AsyncReaderResult<TEnv, T, TError> FromResult<TEnv, T, TError>(Result<T, TError> result) =>
        new((_, _) => Task.FromResult(result));

    /// <summary>Runs the synchronous reader in the supplied environment on each execution.</summary>
    public static AsyncReaderResult<TEnv, T, TError> FromReaderResult<TEnv, T, TError>(ReaderResult<TEnv, T, TError> reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return new((env, _) => Task.FromResult(reader.Run(env)));
    }
}
