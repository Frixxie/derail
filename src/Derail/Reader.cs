namespace Derail;

/// <summary>A lazy, reusable computation that receives an explicit environment.</summary>
/// <remarks>Each run invokes the computation again. Purity and thread safety depend on its delegate and environment.</remarks>
public sealed class Reader<TEnv, T>
{
    private readonly Func<TEnv, T> _run;

    /// <summary>Wraps a computation without running it.</summary>
    public Reader(Func<TEnv, T> run)
    {
        ArgumentNullException.ThrowIfNull(run);
        _run = run;
    }

    /// <summary>Runs the computation against the supplied environment.</summary>
    public T Run(TEnv environment) => _run(environment);

    /// <summary>Lazily transforms the computed value.</summary>
    public Reader<TEnv, TResult> Map<TResult>(Func<T, TResult> map) => new(env => map(Run(env)));

    /// <summary>Chains computations using the same environment.</summary>
    public Reader<TEnv, TResult> Bind<TResult>(Func<T, Reader<TEnv, TResult>> bind) =>
        new(env => bind(Run(env)).Run(env));

    /// <summary>Runs both computations left-to-right against the same environment.</summary>
    public Reader<TEnv, TResult> Zip<TOther, TResult>(Reader<TEnv, TOther> other, Func<T, TOther, TResult> combine) =>
        new(env => combine(Run(env), other.Run(env)));

    /// <summary>Adapts an outer environment to the environment this computation needs.</summary>
    public Reader<TOuter, T> Local<TOuter>(Func<TOuter, TEnv> project) => new(env => Run(project(env)));

    /// <summary>LINQ support: <c>select</c>.</summary>
    public Reader<TEnv, TResult> Select<TResult>(Func<T, TResult> map) => Map(map);

    /// <summary>LINQ support: multiple <c>from</c> clauses.</summary>
    public Reader<TEnv, TResult> SelectMany<TNext, TResult>(
        Func<T, Reader<TEnv, TNext>> bind, Func<T, TNext, TResult> project) =>
        Bind(value => bind(value).Map(next => project(value, next)));
}

/// <summary>Factory methods for environment-dependent computations.</summary>
public static class Reader
{
    /// <summary>Reads the environment itself.</summary>
    public static Reader<TEnv, TEnv> Ask<TEnv>() => new(env => env);

    /// <summary>Creates a computation that always returns the supplied value.</summary>
    /// <remarks>Reference values are shared between runs, not cloned.</remarks>
    public static Reader<TEnv, T> Pure<TEnv, T>(T value) => new(_ => value);
}
