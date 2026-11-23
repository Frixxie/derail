using Derail;
using Number = Derail.Option<int>;

static class Options
{
    static Number[] Initialized() => [Option.Some(42), Option.None, new(new Some<int>(1)), new(new None())];
    static Number? NoNullableValue() => default;
    static Number[] ArrayStorage() => new Number[2];
    static Other.Option<int> Unrelated() => default;
#if INVALID
    static Number TypedDefault() => default(Number); // DRL001
    static Number TargetTypedDefault() => default; // DRL001
    static Number Parameterless() => new Number(); // DRL001
    static Number TargetTypedNew() => new(); // DRL001
#endif
}

namespace Other { readonly struct Option<T>; }
