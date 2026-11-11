namespace Derail;

/// <summary>A present value.</summary>
public readonly record struct Some<T>(T Value);

/// <summary>The absence of a value.</summary>
public readonly record struct None;

/// <summary>An optional value: either <see cref="Some{T}"/> or <see cref="None"/>.</summary>
public union Option<T>(Some<T>, None);

/// <summary>Factory methods for <see cref="Option{T}"/>.</summary>
public static class Option
{
    /// <summary>The absent value. Converts implicitly to any <see cref="Option{T}"/>.</summary>
    public static None None => default;

    /// <summary>Wraps a present value.</summary>
    public static Option<T> Some<T>(T value) => new Some<T>(value);

    /// <summary>Converts a nullable reference to an option.</summary>
    public static Option<T> FromNullable<T>(T? value) where T : class =>
        value is null ? None : Some(value);

    /// <summary>Converts a nullable value to an option.</summary>
    public static Option<T> FromNullable<T>(T? value) where T : struct =>
        value is { } v ? Some(v) : None;
}

/// <summary>Combinators for <see cref="Option{T}"/>.</summary>
public static class OptionExtensions
{
    extension<T>(Option<T> option)
    {
        /// <summary>True when a value is present.</summary>
        public bool IsSome => option is Some<T>;

        /// <summary>True when no value is present.</summary>
        public bool IsNone => option is None;

        /// <summary>Folds the option into a single value.</summary>
        public TResult Match<TResult>(Func<T, TResult> some, Func<TResult> none) =>
            option switch
            {
                Some<T> s => some(s.Value),
                None => none(),
            };

        /// <summary>Transforms the value when present.</summary>
        public Option<TResult> Map<TResult>(Func<T, TResult> map) =>
            option.Match(v => Option.Some(map(v)), () => Option.None);

        /// <summary>Chains an option-returning function when a value is present.</summary>
        public Option<TResult> Bind<TResult>(Func<T, Option<TResult>> bind) =>
            option.Match(bind, () => Option.None);

        /// <summary>Keeps the value only when it satisfies the predicate.</summary>
        public Option<T> Where(Func<T, bool> predicate) =>
            option.Bind(v => predicate(v) ? Option.Some(v) : Option.None);

        /// <summary>Returns the value, or <paramref name="fallback"/> when absent.</summary>
        public T GetValueOrDefault(T fallback) => option.Match(v => v, () => fallback);

        /// <summary>Converts to a result, using <paramref name="error"/> when absent.</summary>
        public Result<T, TError> ToResult<TError>(Func<TError> error) =>
            option.Match(Result.Ok<T, TError>, () => Result.Error<T, TError>(error()));

        /// <summary>LINQ support: <c>select</c>.</summary>
        public Option<TResult> Select<TResult>(Func<T, TResult> map) => option.Map(map);

        /// <summary>LINQ support: multiple <c>from</c> clauses.</summary>
        public Option<TResult> SelectMany<TNext, TResult>(
            Func<T, Option<TNext>> bind,
            Func<T, TNext, TResult> project) =>
            option.Bind(v => bind(v).Map(n => project(v, n)));

        /// <summary>Combines two options; <see cref="None"/> when either is absent.</summary>
        public Option<TResult> Zip<TOther, TResult>(Option<TOther> other, Func<T, TOther, TResult> combine) =>
            option.Bind(a => other.Map(b => combine(a, b)));

        /// <summary>Converts to a validation, using <paramref name="error"/> when absent.</summary>
        public Validation<T, TError> ToValidation<TError>(Func<TError> error) =>
            option.Match(Validation.Valid<T, TError>, () => Validation.Invalid<T, TError>(error()));
    }

    extension<T, TResult>(Option<Func<T, TResult>> function)
    {
        /// <summary>Applies a wrapped function to a wrapped value.</summary>
        public Option<TResult> Apply(Option<T> argument) => function.Zip(argument, (f, x) => f(x));
    }
}
