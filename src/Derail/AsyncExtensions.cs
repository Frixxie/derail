namespace Derail;

/// <summary>Async combinators for <see cref="Result{T, TError}"/> and <see cref="Option{T}"/>.</summary>
/// <remarks>
/// Methods on a value take an async function.
/// Methods on a <see cref="Task{TResult}"/> or <see cref="ValueTask{TResult}"/> continue a pipeline that is already async; the name says whether the function is.
/// Library awaits do not request the caller's synchronization context. Exceptions and cancellation propagate to the caller.
/// </remarks>
public static partial class AsyncExtensions
{
    extension<T, TError>(Result<T, TError> result)
    {
        /// <summary>Transforms the success value with an async function.</summary>
        public async Task<Result<TResult, TError>> MapAsync<TResult>(Func<T, Task<TResult>> map) =>
            result switch
            {
                Ok<T>(var value) => Result.Ok<TResult, TError>(await map(value).ConfigureAwait(false)),
                Error<TError> e => e,
            };

        /// <summary>Chains an async result-returning function when successful.</summary>
        public Task<Result<TResult, TError>> BindAsync<TResult>(Func<T, Task<Result<TResult, TError>>> bind) =>
            result.Match(bind, e => Task.FromResult(Result.Error<TResult, TError>(e)));

        /// <summary>Awaits an action on success, then returns the original result.</summary>
        public async Task<Result<T, TError>> TapAsync(Func<T, Task> action)
        {
            switch (result)
            {
                case Ok<T>(var value):
                    await action(value).ConfigureAwait(false);
                    return result;
                case Error<TError>:
                    return result;
                default:
                    throw new System.Runtime.CompilerServices.SwitchExpressionException(result);
            }
        }

        /// <summary>Awaits a predicate on success and creates an error only if it returns false.</summary>
        public async Task<Result<T, TError>> EnsureAsync(Func<T, Task<bool>> predicate, Func<T, TError> errorFactory) =>
            result switch
            {
                Ok<T>(var value) => await predicate(value).ConfigureAwait(false) ? result : new Error<TError>(errorFactory(value)),
                Error<TError> => result,
            };
    }

    extension<T, TError>(Task<Result<T, TError>> task)
    {
        /// <summary>Transforms the success value.</summary>
        public async Task<Result<TResult, TError>> Map<TResult>(Func<T, TResult> map) =>
            (await task.ConfigureAwait(false)).Map(map);

        /// <summary>Transforms the success value with an async function.</summary>
        public async Task<Result<TResult, TError>> MapAsync<TResult>(Func<T, Task<TResult>> map) =>
            await (await task.ConfigureAwait(false)).MapAsync(map).ConfigureAwait(false);

        /// <summary>Transforms the error value.</summary>
        public async Task<Result<T, TNewError>> MapError<TNewError>(Func<TError, TNewError> map) =>
            (await task.ConfigureAwait(false)).MapError(map);

        /// <summary>Chains a result-returning function when successful.</summary>
        public async Task<Result<TResult, TError>> Bind<TResult>(Func<T, Result<TResult, TError>> bind) =>
            (await task.ConfigureAwait(false)).Bind(bind);

        /// <summary>Chains an async result-returning function when successful.</summary>
        public async Task<Result<TResult, TError>> BindAsync<TResult>(Func<T, Task<Result<TResult, TError>>> bind) =>
            await (await task.ConfigureAwait(false)).BindAsync(bind).ConfigureAwait(false);

        /// <summary>Awaits an action on success, then returns the original result.</summary>
        public async Task<Result<T, TError>> TapAsync(Func<T, Task> action) =>
            await (await task.ConfigureAwait(false)).TapAsync(action).ConfigureAwait(false);

        /// <summary>Awaits a predicate on success and creates an error only if it returns false.</summary>
        public async Task<Result<T, TError>> EnsureAsync(Func<T, Task<bool>> predicate, Func<T, TError> errorFactory) =>
            await (await task.ConfigureAwait(false)).EnsureAsync(predicate, errorFactory).ConfigureAwait(false);

        /// <summary>Folds the result into a single value.</summary>
        public async Task<TResult> Match<TResult>(Func<T, TResult> ok, Func<TError, TResult> error) =>
            (await task.ConfigureAwait(false)).Match(ok, error);
    }

    extension<T>(Option<T> option)
    {
        /// <summary>Transforms the value with an async function when present.</summary>
        public async Task<Option<TResult>> MapAsync<TResult>(Func<T, Task<TResult>> map) =>
            option is Some<T>(var value) ? Option.Some(await map(value).ConfigureAwait(false)) : Option.None;

        /// <summary>Chains an async option-returning function when a value is present.</summary>
        public Task<Option<TResult>> BindAsync<TResult>(Func<T, Task<Option<TResult>>> bind) =>
            option.Match(bind, () => Task.FromResult<Option<TResult>>(Option.None));
    }

    extension<T>(Task<Option<T>> task)
    {
        /// <summary>Transforms the value when present.</summary>
        public async Task<Option<TResult>> Map<TResult>(Func<T, TResult> map) =>
            (await task.ConfigureAwait(false)).Map(map);

        /// <summary>Transforms the value with an async function when present.</summary>
        public async Task<Option<TResult>> MapAsync<TResult>(Func<T, Task<TResult>> map) =>
            await (await task.ConfigureAwait(false)).MapAsync(map).ConfigureAwait(false);

        /// <summary>Chains an option-returning function when a value is present.</summary>
        public async Task<Option<TResult>> Bind<TResult>(Func<T, Option<TResult>> bind) =>
            (await task.ConfigureAwait(false)).Bind(bind);

        /// <summary>Chains an async option-returning function when a value is present.</summary>
        public async Task<Option<TResult>> BindAsync<TResult>(Func<T, Task<Option<TResult>>> bind) =>
            await (await task.ConfigureAwait(false)).BindAsync(bind).ConfigureAwait(false);

        /// <summary>Folds the option into a single value.</summary>
        public async Task<TResult> Match<TResult>(Func<T, TResult> some, Func<TResult> none) =>
            (await task.ConfigureAwait(false)).Match(some, none);

        /// <summary>Converts to a result, using <paramref name="error"/> when absent.</summary>
        public async Task<Result<T, TError>> ToResult<TError>(Func<TError> error) =>
            (await task.ConfigureAwait(false)).ToResult(error);
    }
}
