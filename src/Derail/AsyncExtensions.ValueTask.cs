using System.Runtime.CompilerServices;

namespace Derail;

public static partial class AsyncExtensions
{
    // On a synchronous receiver, keep Task as the default for untyped async lambdas.
    extension<T, TError>(Result<T, TError> result)
    {
        /// <summary>Runs a ValueTask-returning replacement only on error.</summary>
        [OverloadResolutionPriority(-1)]
        public ValueTask<Result<T, TNewError>> RecoverAsync<TNewError>(Func<TError, ValueTask<Result<T, TNewError>>> recover) =>
            result switch
            {
                Ok<T> ok => ValueTask.FromResult<Result<T, TNewError>>(ok),
                Error<TError>(var error) => recover(error),
            };

        /// <summary>Awaits an observation only on error and returns the original result.</summary>
        [OverloadResolutionPriority(-1)]
        public async ValueTask<Result<T, TError>> TapErrorAsync(Func<TError, ValueTask> action)
        {
            switch (result)
            {
                case Ok<T>: return result;
                case Error<TError>(var error):
                    await action(error).ConfigureAwait(false);
                    return result;
                default: throw new SwitchExpressionException(result);
            }
        }

        /// <summary>Transforms the success value with a ValueTask-returning function.</summary>
        [OverloadResolutionPriority(-1)]
        public async ValueTask<Result<TResult, TError>> MapAsync<TResult>(Func<T, ValueTask<TResult>> map) =>
            result switch
            {
                Ok<T>(var value) => Result.Ok<TResult, TError>(await map(value).ConfigureAwait(false)),
                Error<TError> e => e,
            };

        /// <summary>Chains a ValueTask-returning function when successful.</summary>
        [OverloadResolutionPriority(-1)]
        public ValueTask<Result<TResult, TError>> BindAsync<TResult>(Func<T, ValueTask<Result<TResult, TError>>> bind) =>
            result switch
            {
                Ok<T>(var value) => bind(value),
                Error<TError> e => ValueTask.FromResult<Result<TResult, TError>>(e),
            };

        /// <summary>Awaits an observation on success, then returns the original result.</summary>
        [OverloadResolutionPriority(-1)]
        public async ValueTask<Result<T, TError>> TapAsync(Func<T, ValueTask> action)
        {
            switch (result)
            {
                case Ok<T>(var value):
                    await action(value).ConfigureAwait(false);
                    return result;
                case Error<TError>:
                    return result;
                default:
                    throw new SwitchExpressionException(result);
            }
        }

        /// <summary>Awaits a predicate on success and creates an error only if it returns false.</summary>
        [OverloadResolutionPriority(-1)]
        public async ValueTask<Result<T, TError>> EnsureAsync(Func<T, ValueTask<bool>> predicate, Func<T, TError> errorFactory) =>
            result switch
            {
                Ok<T>(var value) => await predicate(value).ConfigureAwait(false) ? result : new Error<TError>(errorFactory(value)),
                Error<TError> => result,
            };
    }

    extension<T, TError>(ValueTask<Result<T, TError>> task)
    {
        /// <summary>Replaces a typed error after awaiting the result.</summary>
        public async ValueTask<Result<T, TNewError>> Recover<TNewError>(Func<TError, Result<T, TNewError>> recover) =>
            (await task.ConfigureAwait(false)).Recover(recover);

        /// <summary>Awaits a replacement computation only on error.</summary>
        public async ValueTask<Result<T, TNewError>> RecoverAsync<TNewError>(Func<TError, ValueTask<Result<T, TNewError>>> recover) =>
            await (await task.ConfigureAwait(false)).RecoverAsync(recover).ConfigureAwait(false);

        /// <summary>Observes a typed error after awaiting the result.</summary>
        public async ValueTask<Result<T, TError>> TapError(Action<TError> action) =>
            (await task.ConfigureAwait(false)).TapError(action);

        /// <summary>Awaits an observation only on error and preserves the result.</summary>
        public async ValueTask<Result<T, TError>> TapErrorAsync(Func<TError, ValueTask> action) =>
            await (await task.ConfigureAwait(false)).TapErrorAsync(action).ConfigureAwait(false);

        /// <summary>Transforms the success value.</summary>
        public async ValueTask<Result<TResult, TError>> Map<TResult>(Func<T, TResult> map) =>
            (await task.ConfigureAwait(false)).Map(map);

        /// <summary>Transforms the success value with an async function.</summary>
        public async ValueTask<Result<TResult, TError>> MapAsync<TResult>(Func<T, ValueTask<TResult>> map) =>
            await (await task.ConfigureAwait(false)).MapAsync(map).ConfigureAwait(false);

        /// <summary>Transforms the error value.</summary>
        public async ValueTask<Result<T, TNewError>> MapError<TNewError>(Func<TError, TNewError> map) =>
            (await task.ConfigureAwait(false)).MapError(map);

        /// <summary>Chains a result-returning function when successful.</summary>
        public async ValueTask<Result<TResult, TError>> Bind<TResult>(Func<T, Result<TResult, TError>> bind) =>
            (await task.ConfigureAwait(false)).Bind(bind);

        /// <summary>Chains an async result-returning function when successful.</summary>
        public async ValueTask<Result<TResult, TError>> BindAsync<TResult>(Func<T, ValueTask<Result<TResult, TError>>> bind) =>
            await (await task.ConfigureAwait(false)).BindAsync(bind).ConfigureAwait(false);

        /// <summary>Awaits an observation on success, then returns the original result.</summary>
        public async ValueTask<Result<T, TError>> TapAsync(Func<T, ValueTask> action) =>
            await (await task.ConfigureAwait(false)).TapAsync(action).ConfigureAwait(false);

        /// <summary>Awaits a predicate on success and creates an error only if it returns false.</summary>
        public async ValueTask<Result<T, TError>> EnsureAsync(Func<T, ValueTask<bool>> predicate, Func<T, TError> errorFactory) =>
            await (await task.ConfigureAwait(false)).EnsureAsync(predicate, errorFactory).ConfigureAwait(false);

        /// <summary>Folds the result into a single value.</summary>
        public async ValueTask<TResult> Match<TResult>(Func<T, TResult> ok, Func<TError, TResult> error) =>
            (await task.ConfigureAwait(false)).Match(ok, error);
    }

    extension<T>(Option<T> option)
    {
        /// <summary>Runs a ValueTask-returning fallback only when absent.</summary>
        [OverloadResolutionPriority(-1)]
        public ValueTask<Option<T>> OrElseAsync(Func<ValueTask<Option<T>>> fallback) =>
            option.IsSome ? ValueTask.FromResult(option) : fallback();

        /// <summary>Transforms the value with a ValueTask-returning function when present.</summary>
        [OverloadResolutionPriority(-1)]
        public async ValueTask<Option<TResult>> MapAsync<TResult>(Func<T, ValueTask<TResult>> map) =>
            option switch
            {
                Some<T>(var value) => Option.Some(await map(value).ConfigureAwait(false)),
                None => Option.None,
            };

        /// <summary>Chains a ValueTask-returning function when a value is present.</summary>
        [OverloadResolutionPriority(-1)]
        public ValueTask<Option<TResult>> BindAsync<TResult>(Func<T, ValueTask<Option<TResult>>> bind) =>
            option switch
            {
                Some<T>(var value) => bind(value),
                None => ValueTask.FromResult<Option<TResult>>(Option.None),
            };
    }

    extension<T>(ValueTask<Option<T>> task)
    {
        /// <summary>Replaces an absent value after awaiting the option.</summary>
        public async ValueTask<Option<T>> OrElse(Func<Option<T>> fallback) =>
            (await task.ConfigureAwait(false)).OrElse(fallback);

        /// <summary>Awaits a fallback only when the option is absent.</summary>
        public async ValueTask<Option<T>> OrElseAsync(Func<ValueTask<Option<T>>> fallback) =>
            await (await task.ConfigureAwait(false)).OrElseAsync(fallback).ConfigureAwait(false);

        /// <summary>Transforms the value when present.</summary>
        public async ValueTask<Option<TResult>> Map<TResult>(Func<T, TResult> map) =>
            (await task.ConfigureAwait(false)).Map(map);

        /// <summary>Transforms the value with an async function when present.</summary>
        public async ValueTask<Option<TResult>> MapAsync<TResult>(Func<T, ValueTask<TResult>> map) =>
            await (await task.ConfigureAwait(false)).MapAsync(map).ConfigureAwait(false);

        /// <summary>Chains an option-returning function when a value is present.</summary>
        public async ValueTask<Option<TResult>> Bind<TResult>(Func<T, Option<TResult>> bind) =>
            (await task.ConfigureAwait(false)).Bind(bind);

        /// <summary>Chains an async option-returning function when a value is present.</summary>
        public async ValueTask<Option<TResult>> BindAsync<TResult>(Func<T, ValueTask<Option<TResult>>> bind) =>
            await (await task.ConfigureAwait(false)).BindAsync(bind).ConfigureAwait(false);

        /// <summary>Folds the option into a single value.</summary>
        public async ValueTask<TResult> Match<TResult>(Func<T, TResult> some, Func<TResult> none) =>
            (await task.ConfigureAwait(false)).Match(some, none);

        /// <summary>Converts to a result, using the error factory when absent.</summary>
        public async ValueTask<Result<T, TError>> ToResult<TError>(Func<TError> error) =>
            (await task.ConfigureAwait(false)).ToResult(error);
    }
}
