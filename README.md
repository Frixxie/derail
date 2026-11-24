# Derail

[![CI](https://github.com/Frixxie/derail/actions/workflows/ci.yml/badge.svg)](https://github.com/Frixxie/derail/actions/workflows/ci.yml)

Derail is a lightweight library of functional programming primitives for C# 15 on .NET 11. Use it when callers need to know why a domain operation failed, or when several independent checks should report their errors together. You can adopt a single type in an existing C# application.

- **`Option<T>`** — a value that may be absent: `Some<T>` or `None`.
- **`Result<T, TError>`** — an operation that may fail: `Ok<T>` or `Error<TError>`. Stops at the first error.
- **`Validation<T, TError>`** — independent checks: `Valid<T>` or `Invalid<TError>`. Collects every error.
- **`NonEmpty<T>`** — an immutable ordered collection with a required first element.

`Option`, `Result`, and `Validation` are native C# unions.

## Requirements

The NuGet package targets `net11.0`. To build the repository, use the .NET 11 SDK specified in `global.json`.

## Installation

```sh
dotnet add package Derail
```

## Versioning

Derail is developed on the `0.x` release line. The public API can evolve between
releases; check the changelog when upgrading.

### Upgrading to 0.8.0

Three changes may affect existing code:

1. **`NonEmpty<T>` dictionary and set keys now compare by ordered elements.** Two
   separately constructed keys with equal elements represent one entry. If your
   keys identify *instances*, opt in to reference equality explicitly:

   ```csharp
   var byInstance = new Dictionary<NonEmpty<string>, int>(ReferenceEqualityComparer.Instance);
   ```

2. **`Task` combinators now follow the same await policy as `ValueTask` combinators.**
   A callback after pending I/O should not update a UI control directly. For
   example, replace `await pending.TapAsync(value => UpdateViewAsync(value))`
   with an await in the UI handler, followed by the update there:

   ```csharp
   var result = await pending; // In the UI handler, retain its synchronization context.
   await result.TapAsync(value => UpdateViewAsync(value));
   ```

3. **Initialize absent options with `Option.None` instead of `default`.** For example,
   use `Option<int> count = Option.None;`. If every slot in an array should start
   absent, use `Array.Fill(counts, Option.None)` after allocating the `Option<int>[]`.
   For a sequence of options, `FirstOrDefault()` on empty input returns an uninitialized
   option without a `DRL001` diagnostic. Use `options.FirstOrDefault(Option.None)`
   so callers can safely `Match` the result. For a dictionary of options, use
   `byId.GetValueOrDefault(id, Option.None)` for the same reason.

If upgrading from before `0.7.0`, note that `Validation.Zip` rejects an uninitialized
operand even alongside an `Invalid`. For pre-`0.3.0` consumers, use
`Result<T, NonEmpty<TError>>` for `Validation.ToResult()` and accept `NonEmpty<TError>`
in the invalid branch of `Match`. See the [changelog](https://github.com/Frixxie/derail/blob/main/CHANGELOG.md) for examples.

### Names, inference, and execution

- `Map` changes the success value; `MapError` changes one result error, and
  `Validation.MapErrors` maps every accumulated error. `Select` and `SelectMany`
  are the C# query-syntax entry points. `GetValueOrElse` returns a plain value;
  `OrElse` and `Recover` continue the computation. These are distinct contracts.
- C# infers `Option.Some(value)` from its argument. A result factory must also
  specify the type of the case it does not construct: `Result.Ok<int, string>(3)`.
  With an expected type, a case constructor is shorter: `Result<int, string> r = new Ok<int>(3);`.
- Nullable annotations describe the **payload**, not the case. `Result.Ok<string?, int>(null)`
  is successful; `Option.FromNullable((string?)null)` is absent. A non-empty
  collection is a reference type and requires a constructed instance.
- Plain values compose immediately. C# evaluates arguments before calling `Zip`;
  a result can skip its combiner but cannot undo work that produced either argument.
  `Traverse` invokes its mapper during one
  ordered pass. See [default initialization](#default-initialization) and [equality](#equality).
- Async `Option` and `Result` pipelines support both `Task` and `ValueTask` for
  dependent I/O. `Map` takes a synchronous callback; `MapAsync` awaits one.
  For a synchronous operation without a wrapper overload, await the input first.
  Validation remains synchronous and applicative.
- Callback exceptions are not converted into typed errors. An async combinator
  that forwards a delegate's task can throw before returning it if that delegate
  throws synchronously. Await the whole call inside the `try` when handling an
  exception. Cancellation remains cancellation; `Recover` only handles typed errors.

## Quick start: from input to outcome

Paste this into `Program.cs` in a .NET 11 console app after adding Derail:

```csharp
using Derail;

Console.WriteLine(Quote("3"));    // Total: 15 credits
Console.WriteLine(Quote("0"));    // Quantity must be positive
Console.WriteLine(Quote("ten"));  // Enter a whole number

static string Quote(string input) =>
    ParseQuantity(input)
        .Ensure(quantity => quantity > 0, _ => "Quantity must be positive")
        .Map(quantity => quantity * 5L)
        .Match(total => $"Total: {total} credits", error => error);

static Result<int, string> ParseQuantity(string input) =>
    int.TryParse(input, out var quantity)
        ? new Ok<int>(quantity)
        : new Error<string>("Enter a whole number");
```

`Ensure` checks the parsed quantity, `Map` calculates the price, and `Match` returns the message. If parsing or the quantity check fails, the calculation is skipped and `Match` receives the first error.

## Intended use

For a rule handled in one place, an `if`, a `switch`, or `TryParse` with an early return is usually clearer. Derail earns its place when several operations compose or an outcome crosses a method boundary:

- **Dependent steps (monadic composition):** `Result.Bind` passes a successful value to the next step. A withdrawal needs an account, so a failed account lookup skips the balance check and preserves the lookup error.
- **Independent checks (applicative composition):** `Validation.Zip` combines checks whose inputs are already available. A missing carrier and a missing tracking number can both be reported in one response. `Validation` deliberately has no `Bind`: there is no successful value to pass to a dependent check after a failure.

Use typed errors for expected outcomes a caller can act on, such as insufficient funds. Keep exceptions for programming mistakes and unexpected infrastructure failures: a database timeout is different from an account that was not found. Async cancellation propagates as cancellation.

Derail supplies values and functions for composing them. Your application still owns dependency injection, transactions, and event delivery.

For readability, aim for **at most one level of indirection**: the pipeline should tell the story, and opening one named step should reveal its rule. For example, `Bind((Account a) => Withdraw(a, amount))` is useful when `Withdraw` contains the balance check. A helper that just forwards to another helper makes that rule harder to find.

Use `Ensure` to reject a successful value that fails a rule, and `Tap` for a synchronous observation such as recording a metric. An existing error skips both. For an async predicate or observation, use `EnsureAsync` or `TapAsync`. If the step returns its own `Result`, use `Bind` or `BindAsync`.

## Quick tour

The examples below come from [`BankingExamples.cs`](tests/Derail.Tests/BankingExamples.cs), where they run as tests.

The examples often spell out types to show what each step receives and returns. In your own code, C# can usually infer them: `.Map((Account a) => a.Balance)` can be written as `.Map(a => a.Balance)`.

### Default initialization

The Derail NuGet package includes a C# analyzer. Build error `DRL001` flags
`default(Option<T>)`, target-typed `default`, and parameterless `new Option<T>()`
or `new()` when the target is an option. Use `Option.None` for absence or
`Option.Some(value)` for presence. Constructors that take a case are also valid.
The same rule covers `Result<T, TError>` and `Validation<T, TError>`, and its message
names the cases to use for each type. The analyzer checks explicit expressions only.
These still compile and fail at run time with `SwitchExpressionException`:

- fields and auto-properties that no constructor or initializer assigns
- slots of a newly allocated array, such as `new Option<int>[n]`
- `default(T)` in generic code where `T` is a Derail union
- `FirstOrDefault()` or `GetValueOrDefault(key)` without a fallback
- serializer output for a missing property, since serializers create the default value

Assign these explicitly, pass a fallback such as `Option.None`, or map serialized data
from nullable DTO properties with `Option.FromNullable`.

These rules apply to `default` and to elements of newly allocated arrays:

| Primitive | Meaning of `default` | Inspection and use |
| --- | --- | --- |
| `Option<T>` | Uninitialized; neither `Some<T>` nor `None` | `IsSome` and `IsNone` are both false. Initialize with `Option.Some(value)` or `Option.None` before use. |
| `Result<T, TError>` | Uninitialized; neither `Ok` nor `Error` | `IsOk` and `IsError` are both false. Initialize with `Result.Ok` or `Result.Error` before use. |
| `Validation<T, TError>` | Uninitialized; neither `Valid` nor `Invalid` | `IsValid` and `IsInvalid` are both false. Initialize with `Validation.Valid` or `Validation.Invalid` before use. |
| `NonEmpty` | `null` reference | No instance exists. Construct one before calling its members. |

An uninitialized option, result, or validation is a programming error, not a domain outcome.
Combinators such as `Match`, `Map`, and `GetValueOrDefault` throw
`SwitchExpressionException` when they need to inspect its case. `Result.Zip`
stops at a left-hand `Error`, so it never inspects the right-hand result in that
case. `Validation.Zip` inspects both inputs to accumulate errors and rejects an
uninitialized input on either side. `Validation.Sequence` likewise rejects an
uninitialized element even after an `Invalid`.

### Equality

- `Option`, `Result`, and `Validation` compare by case and payload using `Equals`
  or the default .NET equality comparer. For `Invalid`, every error and its
  position must match.
- `NonEmpty<T>` compares by length and element order using
  `EqualityComparer<T>.Default`. `Equals`, `==`, and hash-based collections use
  the same rule. Comparing or hashing can visit every element. As with other
  dictionary keys, keep element equality and hash codes stable while the key is stored.

### Option: values that may be missing

`Option.Some(0)` and `Option.Some<string?>(null)` are both present: `Match`
passes the payload to the `some` callback. Use `Option.FromNullable((string?)null)`
when a null reference should mean `None`.

```csharp
Option<Account> FindAccount(AccountNumber number) =>
    ledger.TryGetValue(number, out Account? account) ? Option.Some(account) : Option.None;

string displayName = Option.FromNullable(account.Nickname).GetValueOrDefault(account.Number.Value);

Option<Account> premium = FindAccount(number).Where((Account a) => a.Balance >= 500m);

Option<decimal> total = FindAccount(savings).Zip(FindAccount(checking), (Account a, Account b) => a.Balance + b.Balance);
```

### Result: pipelines that stop at the first failure

Model errors as a closed hierarchy and let each step return a `Result`:

```csharp
abstract record TransferError
{
    public sealed record MalformedAccountNumber(string Input) : TransferError;
    public sealed record AccountNotFound(AccountNumber Number) : TransferError;
    public sealed record AccountFrozen(AccountNumber Number) : TransferError;
    public sealed record InsufficientFunds(AccountNumber Number, decimal Balance, decimal Requested) : TransferError;
    // ...
}

Result<Account, TransferError> GetAccount(AccountNumber number) =>
    FindAccount(number).ToResult(TransferError () => new TransferError.AccountNotFound(number));
```

This pipeline logs a completed withdrawal and returns the new balance. A frozen account or insufficient funds skips the success log:

```csharp
TextWriter log = Console.Out;
Result<decimal, TransferError> balance = ParseAccountNumber(input)
    .Bind(GetAccount)
    .Ensure((Account a) => !a.Frozen, (Account a) => new TransferError.AccountFrozen(a.Number))
    .Bind((Account a) => Withdraw(a, 100m))
    .Tap((Account _) => log.WriteLine("Withdrawal completed"))
    .Map((Account a) => a.Balance);
```

Use `Zip` to combine two results you already have. In `first.Zip(GetSecond(), combine)`, C# calls `GetSecond()` even if `first` is an error; only `combine` is skipped. Use `first.Bind(a => GetSecond().Map(b => combine(a, b)))` when the second operation should run only after the first succeeds.

When later steps need earlier values, use query syntax:

```csharp
Result<Receipt, TransferError> Transfer(string fromInput, string toInput, decimal amount) =>
    from value in EnsurePositive(amount)
    from source in OpenAccount(fromInput)
    from target in OpenAccount(toInput)
    from _ in EnsureDifferent(source, target)
    from debited in Withdraw(source, value)
    let credited = Deposit(target, value)
    select new Receipt(source.Number, target.Number, value, debited.Balance, credited.Balance);
```

Handle the outcome with pattern matching, or translate errors at the boundary:

```csharp
string message = Transfer(from, to, 200m) switch
{
    Ok<Receipt>(var r) => $"Moved {r.Amount:0.00} from {r.From} to {r.To}",
    Error<TransferError>(TransferError.InsufficientFunds(var n, var balance, var requested)) =>
        $"Account {n} has {balance:0.00}, needs {requested:0.00}",
    // ...
};

int status = Transfer(from, to, amount)
    .MapError((TransferError error) => error switch
    {
        TransferError.AccountNotFound => 404,
        TransferError.AccountFrozen => 423,
        TransferError.InsufficientFunds => 409,
        _ => 400,
    })
    .Match((Receipt _) => 200, (int code) => code);
```

### Validation: report every problem at once

```csharp
Validation<AccountApplication, string> ValidateApplication(string owner, string number, decimal deposit) =>
    ValidateOwner(owner).Zip(ValidateNumber(number), ValidateDeposit(deposit),
        (string o, AccountNumber n, decimal d) => new AccountApplication(o, n, d));

ValidateApplication("", "12", 10m);
// Invalid: "Owner is required", "'12' is not an account number",
//          "Initial deposit must be at least 100.00, was 10.00"
```

For four independent fields, pass the other three validations and a constructor
function to `Zip`. The [parcel example](tests/Derail.Tests/Examples/Parcel.cs) avoids
carrying a tuple of dimensions into a second `Zip`:

```csharp
var parcel = length.Zip(width, height, weight, (l, w, h, kg) => new Parcel(l, w, h, kg));
```

Errors follow argument order; the constructor runs only when every field is valid.
`Validation.Sequence` checks a whole batch, and `ToResult()` hands a validated value back to a `Result` pipeline:

```csharp
Validation<IReadOnlyList<AccountNumber>, string> numbers = Validation.Sequence(batch.Select(ValidateNumber));

Result<AccountApplication, NonEmpty<string>> application =
    ValidateApplication("", "12", 500m).ToResult();

string message = application
    .Map((AccountApplication a) => $"Welcome, {a.Owner}")
    .Match((string ok) => ok, (NonEmpty<string> errors) => string.Join("; ", errors));
// "Owner is required; '12' is not an account number"
```

Failures always have a first error, so an error summary needs no empty-list fallback:

```csharp
string summary = ValidateApplication("", "12", 10m).Match(
    (AccountApplication _) => "Application accepted",
    (NonEmpty<string> errors) => $"{errors.Head} ({errors.Tail.Count} more problems)");
// "Owner is required (2 more problems)"
```

`Invalid<TError>` is a sealed class whose `Errors` is a `NonEmpty<TError>`: every
constructed failure contains at least one error. `Match` receives that non-empty
collection, and `ToResult()` returns `Result<T, NonEmpty<TError>>`. Mapping and
combining failures preserve non-emptiness and error order.

**Breaking API change:** `Invalid<TError>` was previously a struct with an
`IReadOnlyList<TError>` payload. Its `Errors`, deconstruction, and `Match` now expose
`NonEmpty<TError>`; update explicitly typed `ToResult()` consumers accordingly.
`default(Invalid<TError>)` is now null, not an empty failure. Create failures with
`Validation.Invalid<T, TError>(firstError, ...)` or `new Invalid<TError>(firstError, ...)`.

### NonEmpty: require at least one item

```csharp
sealed record Payment(AccountNumber To, decimal Amount);

Result<NonEmpty<Payment>, string> RequirePayments(IEnumerable<Payment> payments) =>
    NonEmpty.FromEnumerable(payments).ToResult(() => "At least one payment is required");

Result<NonEmpty<Payment>, string> batch = RequirePayments([
    new Payment(Checking.Number, 200m),
    new Payment(Seized.Number, 50m),
]);

Result<decimal, string> total = batch.Map((NonEmpty<Payment> payments) =>
    payments.Map((Payment payment) => payment.Amount).Sum()); // Ok(250m)
Result<NonEmpty<Payment>, string> empty = RequirePayments([]); // Error("At least one payment is required")
```

`NonEmpty<T>` implements `IReadOnlyList<T>` with `Head`, `Tail`, and `Count`.
Construction snapshots the input; `Map` and `Append` preserve order and non-emptiness.
It is a sealed class: `default` is null, not a constructed empty collection.
Ordinary LINQ operations such as `Where` still return ordinary sequences that may be empty.
Non-emptiness alone does not validate payment amounts or destination accounts.

### Async

#### Async combinators on Option and Result

`MapAsync` and `BindAsync` accept functions returning `Task` or `ValueTask`. The same combinators work directly on a `Result` or `Option` wrapped in either type:

```csharp
Task<Result<decimal, TransferError>> BalanceInAsync(string input, string currency, TextWriter audit) =>
    ParseAccountNumber(input)
        .BindAsync((AccountNumber n) => FindAccountAsync(n).ToResult(TransferError () => new TransferError.AccountNotFound(n)))
        .EnsureAsync(IsActiveAsync, (Account a) => new TransferError.AccountFrozen(a.Number))
        .MapAsync(async (Account a) => a.Balance * await ExchangeRateAsync(currency))
        .TapAsync((decimal _) => audit.WriteLineAsync("Balance converted"));
```

On a plain `Option` or `Result`, an ordinary `async` lambda starts a `Task` pipeline. Pass a `ValueTask`-returning function to start a `ValueTask` pipeline instead. Once started, the pipeline keeps its task type:

```csharp
ValueTask<Option<Account>> FindCachedAsync(AccountNumber number) => ValueTask.FromResult(FindAccount(number));

ValueTask<Result<decimal, TransferError>> balance = FindCachedAsync(number)
    .Map((Account account) => account.Balance)
    .ToResult(TransferError () => new TransferError.AccountNotFound(number));
```

`EnsureAsync` awaits a success predicate and calls the synchronous error factory only when the predicate returns `false`. `TapAsync` awaits the observation and returns the original result. Both skip their callbacks on an existing error.

### Recovery and fallback

To leave the pipeline with a plain value, use `GetValueOrDefault(value)` for an
already available fallback or `GetValueOrElse` to compute one only when needed.
For example, `nickname.GetValueOrElse(() => LoadDisplayName())` calls
`LoadDisplayName` only for `None`. On a `Result`, the callback receives the original
error; on a `Validation`, it receives all errors in encounter order as a `NonEmpty<TError>`.

Use `OrElse` when the fallback also returns an option. A cache hit (including
`Some(null)`) skips the fallback entirely. Use `TapError` to observe a failure
without replacing it, and `Recover` when the next step can succeed or return a
different error type. For example:

```csharp
Task<Result<Account, LookupError>> FindWithBackup(AccountNumber number) =>
    FindInCache(number)                          // Option<Account>
        .OrElseAsync(() => FindAccountAsync(number))
        .ToResult(() => number)                  // missing account number
        .TapError(missing => RecordCacheAndDatabaseMiss(missing))
        .RecoverAsync(missing => FindInBackupAsync(missing));
```

Use `TapErrorAsync` for an observer that must be awaited. Recovery handles typed
errors and absence; it does not catch exceptions or cancellation.

### Async execution policy

Derail uses `ConfigureAwait(false)` for its own `Task` and `ValueTask` awaits.
Callbacks can run inline when an input is already complete; after a pending input,
do not assume the callback runs on your UI thread. Your own `await` controls where
your code resumes. Await each `ValueTask` only once.

Faults and cancellation from inputs or callbacks propagate to the caller; neither
becomes `None` or a typed `Error`. To cancel an operation, pass your token to the
underlying async call inside the callback.

### Removing one layer of nesting

Use `Flatten()` in place of `Bind(inner => inner)` when both layers are the same
primitive. For example, `Option.Some(Option.Some(3)).Flatten()` returns `Some(3)`;
`None` at either level remains `None`. A nested `Result` keeps the first error.

### Collecting a sequence of values

Use `Sequence` when each element is already an `Option`, `Result`, or `Validation`.
Use `Traverse` when a function produces one for each input:

```csharp
Result<IReadOnlyList<int>, string> quantities = Result.Traverse(inputs, ParseQuantity);
```

Both collect immutable snapshots in input order. Empty input succeeds with an empty
list. `Option` stops reading on `None`; `Result` stops on the first `Error`.
`Validation` reads every element and collects all errors in encounter order.
`Validation.Traverse` over a `NonEmpty<T>` returns a `NonEmpty` of values, so validating
a required list keeps the guarantee that it has at least one element.

## API overview

| | `Option<T>` | `Result<T, TError>` | `Validation<T, TError>` |
| --- | --- | --- | --- |
| Create | `Option.Some`, `Option.None`, `Option.FromNullable` | `Result.Ok`, `Result.Error` | `Validation.Valid`, `Validation.Invalid` |
| Inspect | `IsSome`, `IsNone` | `IsOk`, `IsError`, `Tap`, `TapError` | `IsValid`, `IsInvalid` |
| Transform | `Map`, `Where` | `Map`, `MapError` | `Map`, `MapErrors` |
| Chain | `Bind`, LINQ | `Bind`, `Ensure`, LINQ | — (use `ToResult`) |
| Recover | `OrElse` | `Recover` | — |
| Combine | `Zip`, `Apply` | `Zip`, `Apply` (first error) | `Zip` (2–4 inputs), `Apply` (all errors) |
| Collect | `Option.Sequence`, `Option.Traverse` | `Result.Sequence`, `Result.Traverse` | `Validation.Sequence`, `Validation.Traverse` |
| Exit | `Match`, `GetValueOrDefault`, `GetValueOrElse` | `Match`, `GetValueOrDefault`, `GetValueOrElse` | `Match`, `GetValueOrDefault`, `GetValueOrElse` |
| Convert | `ToResult`, `ToValidation` | `ToOption`, `ToValidation` | `ToResult` |
| Async (`Task` or `ValueTask`) | `MapAsync`, `BindAsync`, `OrElseAsync`, wrapped `Option<T>` extensions | `MapAsync`, `BindAsync`, `EnsureAsync`, `TapAsync`, `RecoverAsync`, `TapErrorAsync`, wrapped `Result<T, TError>` extensions | — |

## Build, test and benchmark

The [orders and shipments API](examples/Orders/README.md) uses every primitive
in an ASP.NET Core API backed by PostgreSQL and DbReader.

```sh
dotnet build
dotnet test --project tests/Derail.Tests
dotnet test --project tests/Derail.Analyzers.Tests
dotnet run -c Release --project benchmarks/Derail.Benchmarks
```

See [the benchmark guide](benchmarks/README.md) for focused runs, baselines,
allocation measurements, and improvement candidates.

### Dev container

Open the repository in a Dev Containers-compatible editor and choose **Reopen in Container**.
The container uses .NET SDK `11.0.100`, matching `global.json`, runs as a non-root user,
and restores packages on creation. Run the commands above in its terminal.

Without an editor or a locally installed SDK, use the same image through Docker:

```sh
docker build -t derail-dev .devcontainer
docker run --rm --init --user "$(id -u):$(id -g)" \
  --env DOTNET_CLI_HOME=/tmp/dotnet --env NUGET_PACKAGES=/tmp/nuget \
  --mount type=bind,source="$PWD",target=/workspaces/derail \
  derail-dev dotnet test --project tests/Derail.Tests
```

`--init` keeps the test runner out of PID 1 and allows it to exit normally.
The explicit user prevents root-owned build files on Linux; the CLI home and package cache
are disposable directories inside the container. Only the repository is mounted from the host.

## Layout

```
src/Derail/                  Library: Option, Result, Validation, NonEmpty, async extensions
src/Derail.Analyzers/        Roslyn analyzer shipped in the package (DRL001)
tests/Derail.Tests/          Unit, property-based and example tests
tests/Derail.Analyzers.Tests Analyzer tests against in-process compilations
examples/Orders/             ASP.NET Core and PostgreSQL example API
examples/Orders.Tests/       Example integration tests (requires Docker)
benchmarks/Derail.Benchmarks BenchmarkDotNet benchmarks
```

## Releasing

See [the changelog](https://github.com/Frixxie/derail/blob/main/CHANGELOG.md) for release notes and migration details.

Update the package version in `src/Derail/Derail.csproj`, then push the matching tag
(for example, `v0.3.0`). The [release workflow](.github/workflows/release.yml) builds,
tests, packs with that version, publishes to nuget.org through trusted publishing,
and creates a GitHub release.

## License

[MIT](LICENSE)
