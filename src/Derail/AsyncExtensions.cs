namespace Derail;

/// <summary>Async combinators for <see cref="Result{T, TError}"/> and <see cref="Option{T}"/>.</summary>
/// <remarks>
/// Methods on a value take an async function (<c>MapAsync</c>, <c>BindAsync</c>).
/// Methods on a <see cref="Task{TResult}"/> continue a pipeline that is already async; the name says whether the function is.
/// </remarks>
public static class AsyncExtensions
{
    extension<T, TError>(Result<T, TError> result)
    {
        /// <summary>Transforms the success value with an async function.</summary>
        public async Task<Result<TResult, TError>> MapAsync<TResult>(Func<T, Task<TResult>> map) =>
            result switch
            {
                Ok<T>(var value) => Result.Ok<TResult, TError>(await map(value)),
                Error<TError> e => e,
            };

        /// <summary>Chains an async result-returning function when successful.</summary>
        public Task<Result<TResult, TError>> BindAsync<TResult>(Func<T, Task<Result<TResult, TError>>> bind) =>
            result.Match(bind, e => Task.FromResult(Result.Error<TResult, TError>(e)));
    }

    extension<T, TError>(Task<Result<T, TError>> task)
    {
        /// <summary>Transforms the success value.</summary>
        public async Task<Result<TResult, TError>> Map<TResult>(Func<T, TResult> map) =>
            (await task).Map(map);

        /// <summary>Transforms the success value with an async function.</summary>
        public async Task<Result<TResult, TError>> MapAsync<TResult>(Func<T, Task<TResult>> map) =>
            await (await task).MapAsync(map);

        /// <summary>Transforms the error value.</summary>
        public async Task<Result<T, TNewError>> MapError<TNewError>(Func<TError, TNewError> map) =>
            (await task).MapError(map);

        /// <summary>Chains a result-returning function when successful.</summary>
        public async Task<Result<TResult, TError>> Bind<TResult>(Func<T, Result<TResult, TError>> bind) =>
            (await task).Bind(bind);

        /// <summary>Chains an async result-returning function when successful.</summary>
        public async Task<Result<TResult, TError>> BindAsync<TResult>(Func<T, Task<Result<TResult, TError>>> bind) =>
            await (await task).BindAsync(bind);

        /// <summary>Folds the result into a single value.</summary>
        public async Task<TResult> Match<TResult>(Func<T, TResult> ok, Func<TError, TResult> error) =>
            (await task).Match(ok, error);
    }

    extension<T>(Option<T> option)
    {
        /// <summary>Transforms the value with an async function when present.</summary>
        public async Task<Option<TResult>> MapAsync<TResult>(Func<T, Task<TResult>> map) =>
            option is Some<T>(var value) ? Option.Some(await map(value)) : Option.None;

        /// <summary>Chains an async option-returning function when a value is present.</summary>
        public Task<Option<TResult>> BindAsync<TResult>(Func<T, Task<Option<TResult>>> bind) =>
            option.Match(bind, () => Task.FromResult<Option<TResult>>(Option.None));
    }

    extension<T>(Task<Option<T>> task)
    {
        /// <summary>Transforms the value when present.</summary>
        public async Task<Option<TResult>> Map<TResult>(Func<T, TResult> map) =>
            (await task).Map(map);

        /// <summary>Transforms the value with an async function when present.</summary>
        public async Task<Option<TResult>> MapAsync<TResult>(Func<T, Task<TResult>> map) =>
            await (await task).MapAsync(map);

        /// <summary>Chains an option-returning function when a value is present.</summary>
        public async Task<Option<TResult>> Bind<TResult>(Func<T, Option<TResult>> bind) =>
            (await task).Bind(bind);

        /// <summary>Chains an async option-returning function when a value is present.</summary>
        public async Task<Option<TResult>> BindAsync<TResult>(Func<T, Task<Option<TResult>>> bind) =>
            await (await task).BindAsync(bind);

        /// <summary>Folds the option into a single value.</summary>
        public async Task<TResult> Match<TResult>(Func<T, TResult> some, Func<TResult> none) =>
            (await task).Match(some, none);

        /// <summary>Converts to a result, using <paramref name="error"/> when absent.</summary>
        public async Task<Result<T, TError>> ToResult<TError>(Func<TError> error) =>
            (await task).ToResult(error);
    }
}
