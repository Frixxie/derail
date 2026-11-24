---
name: derail
description: Guides C# agents in choosing and composing Derail's Option, Result, Validation, and NonEmpty types. Use when implementing or reviewing code that uses the Derail NuGet package, adopting it for a workflow, or diagnosing DRL001.
---

# Use Derail in C#

Check the consuming project's Derail version before choosing an overload. Import `Derail` for the types and extension methods. Start with the outcome the caller needs; a local `if` that never crosses a method boundary rarely needs a wrapper.

| What the caller needs | Type | How to compose it |
| --- | --- | --- |
| A lookup where absence is normal | `Option<T>` | Return `Option.Some(value)` or `Option.None`. Use `OrElse(() => backup())` for a lazy fallback. |
| The first reason a workflow cannot continue | `Result<T, TError>` | Return `new Ok<T>(value)` or `new Error<TError>(error)`. Use `Bind` when the next step returns another result with the same error type. |
| Every problem with independent inputs | `Validation<T, TError>` | Return `Valid<T>` or `Invalid<TError>`. Combine checks with `Zip`; use `AndThen` only for a check that depends on a valid value. |
| A collection guaranteed to have a first element | `NonEmpty<T>` | `NonEmpty.FromEnumerable(items)` returns an option. Convert absence to a domain error with `ToResult(() => error)` if empty input is invalid. |

## Compose first, handle the outcome once

```csharp
using Derail;

Console.WriteLine(Quote("3")); // 15 credits

static Result<int, string> ParseQuantity(string text) =>
    int.TryParse(text, out var n) ? new Ok<int>(n) : new Error<string>("Enter a whole number");

static string Quote(string text) => ParseQuantity(text)
    .Ensure(n => n > 0, _ => "Quantity must be positive")
    .Map(n => n * 5L)
    .Match(total => $"{total} credits", error => error);
```

Use `Map` when the callback returns a plain value. If it returns a `Result`, `Map` nests the result; use `Bind` instead. `first.Zip(LoadNext(), combine)` calls `LoadNext()` even if `first` failed. Put the call inside `Bind` if it must be skipped on failure. When callers need to distinguish failure reasons, use a domain error type and `Match` at the boundary rather than branching on message text.

## Collect independent errors

```csharp
using Derail;

Console.WriteLine(Positive(0, "width").Zip(Positive(-1, "height"), (w, h) => (long)w * h)
    .Match(area => $"Area: {area}", errors => string.Join("; ", errors)));
// width must be positive; height must be positive

static Validation<int, string> Positive(int n, string field) => n > 0
    ? new Valid<int>(n)
    : new Invalid<string>($"{field} must be positive");
```

`Zip` runs the combining function only when all checks pass and preserves error order otherwise. For a batch, `Validation.Traverse(items, check)` collects every error; `Result.Traverse(items, step)` stops at the first one. To start a fail-fast workflow after validation, `ToResult()` preserves the errors as `NonEmpty<TError>`.

## Handle union cases with a switch expression

Use `Match` for a simple fold. Use `switch` when the shape of the payload also matters, as in the one-error and many-error arms below. Name each case explicitly: `_ => "Not found"` would silently treat an uninitialized option as `None`.

```csharp
using Derail;

static string DisplayName(Option<string> name) => name switch
{
    Some<string>(var value) => value,
    None => "Anonymous",
};

static string DisplayQuote(Result<int, string> quote) => quote switch
{
    Ok<int>(var total) => $"{total} credits",
    Error<string>(var reason) => reason,
};

static string DisplayArea(Validation<long, string> area) => area switch
{
    Valid<long>(var value) => $"Area: {value}",
    Invalid<string>(var errors) when errors.Count == 1 => errors.Head,
    Invalid<string>(var errors) => $"{errors.Head} (+{errors.Tail.Count} more)",
};
```

`Invalid<string>` deconstructs to a `NonEmpty<string>`, so reading `Head` is safe in both failure arms.

## Check the edges

- **Missing versus null:** `Option.Some` can hold a null payload. Use `Option.FromNullable(value)` when null means absent.
- **Uninitialized unions:** `default` and parameterless `new()` are not valid cases of `Option`, `Result`, or `Validation` (DRL001). Also check implicit defaults, such as new arrays or `FirstOrDefault()` on wrapped values. Supply a real case on the empty path.
- **Async:** `Option` and `Result` support `Task` and `ValueTask` with `MapAsync` and `BindAsync`. `Validation` is synchronous. Exceptions and cancellation propagate; model expected domain failures explicitly.
- **Verify the distinction you chose:** for a `Bind` chain, test that a failed first step skips the next one. For `Validation.Zip`, give it two bad inputs and assert both errors in order.
