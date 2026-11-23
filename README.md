# Derail

[![CI](https://github.com/Frixxie/derail/actions/workflows/ci.yml/badge.svg)](https://github.com/Frixxie/derail/actions/workflows/ci.yml)

Derail is a lightweight library of functional programming primitives for C# 15 on .NET 11. Use it when callers need to know why a domain operation failed, or when several independent checks should report their errors together. You can adopt a single type in an existing C# application.

- **`Option<T>`** — a value that may be absent: `Some<T>` or `None`.
- **`Result<T, TError>`** — an operation that may fail: `Ok<T>` or `Error<TError>`. Stops at the first error.
- **`Validation<T, TError>`** — independent checks: `Valid<T>` or `Invalid<TError>`. Collects every error.
- **`NonEmpty<T>`** — an immutable ordered collection with a required first element.
- **`Writer<T, TEvent>`** — a value paired with ordered events, without emitting them.
- **`Reader<TEnv, T>` / `ReaderResult<TEnv, T, TError>`** — reusable computations with explicit dependencies.
- **`AsyncReaderResult<TEnv, T, TError>`** — a reusable async workflow that receives its dependencies and cancellation token at execution.

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

### Unreleased changes since 0.7.0

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
  Similarly, a constant reader factory needs the environment type even though its value does not use it.
- Nullable annotations describe the **payload**, not the case. `Result.Ok<string?, int>(null)`
  is successful; `Option.FromNullable((string?)null)` is absent. Reference-type
  readers, writers, and non-empty collections require constructed instances.
- Plain values compose immediately. C# evaluates arguments before calling `Zip`;
  a result can skip its combiner but cannot undo work that produced either argument.
  Reader `Zip` instead runs its computations left to right at `Run`; a failing
  reader skips the right computation. `Traverse` invokes its mapper during one
  ordered pass. See [default initialization](#default-initialization) and [equality](#equality).
- Async `Option` and `Result` pipelines support both `Task` and `ValueTask` for
  dependent I/O. `Map` takes a synchronous callback; `MapAsync` awaits one.
  For a synchronous operation without a wrapper overload, await the input first.
  `AsyncReaderResult.Run` uses `Task`; validation remains synchronous and applicative.
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

Derail supplies values and functions for composing them. Your application still owns dependency injection, transactions, and event delivery: a `Reader` receives the environment you pass to `Run`, and a `Writer` collects events until you decide what to do with them.

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
The analyzer checks explicit expressions; it cannot detect every zero-initialized
field or array slot.

These rules apply to `default` and to elements of newly allocated arrays:

| Primitive | Meaning of `default` | Inspection and use |
| --- | --- | --- |
| `Option<T>` | Uninitialized; neither `Some<T>` nor `None` | `IsSome` and `IsNone` are both false. Initialize with `Option.Some(value)` or `Option.None` before use. |
| `Result<T, TError>` | Uninitialized; neither `Ok` nor `Error` | `IsOk` and `IsError` are both false. Initialize with `Result.Ok` or `Result.Error` before use. |
| `Validation<T, TError>` | Uninitialized; neither `Valid` nor `Invalid` | `IsValid` and `IsInvalid` are both false. Initialize with `Validation.Valid` or `Validation.Invalid` before use. |
| `NonEmpty`, `Reader`, `ReaderResult`, `AsyncReaderResult`, `Writer` | `null` reference | No instance exists. Construct one before calling its members. |

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
- `Writer` uses reference equality. Comparing or hashing a writer does not walk
  or materialize its event history. To compare outcomes, compare `Value` and
  use `left.Events.SequenceEqual(right.Events)`; reading `Events` can allocate
  the flattened event list on first access.
- `Reader`, `ReaderResult`, and `AsyncReaderResult` also use reference equality. Equality never runs
  a computation. To compare outcomes for a particular environment, run both
  readers with that environment and compare their results.

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

### Writer: return events instead of emitting them

```csharp
sealed record TransferRecorded(AccountNumber From, AccountNumber To, decimal Amount);

Result<Writer<Receipt, TransferRecorded>, TransferError> result = Transfer("1234.56.78901", "1098.76.54321", 200m)
    .Map((Receipt receipt) => new Writer<Receipt, TransferRecorded>(receipt,
        [new TransferRecorded(receipt.From, receipt.To, receipt.Amount)]));

// Ok: a receipt paired with one audit event. Error: no Writer is produced.
```

Writer composition is eager, but events are only data: nothing is logged or published.
`Map` preserves events; `Bind` and `Zip` append them left-to-right; `MapEvents` transforms them.
`Writer.Pure<T, TEvent>(value)` starts without events, and `Writer.Tell(event)` records
one event with a `ValueTuple` result. Event inputs are snapshotted, and callers own delivery policy.
The banking workflow only calculates a transfer proposal: it does not persist balances or publish
events. Production code must coordinate persistence and event delivery separately.

#### Which events survive a failure?

Choose the outer type based on what the caller needs when a later step fails.
The [stock reservation example](tests/Derail.Tests/WriterFailureExamples.cs) checks
stock, then either proposes reserving one unit or returns `OutOfStock`:

| Return type | Three units available | Out of stock |
| --- | --- | --- |
| `Writer<Result<int, ReservationError>, Step>` | `Ok(2)` with `[StockChecked, ReservationProposed]` | `Error(OutOfStock)` with `[StockChecked]` |
| `Result<Writer<int, Step>, ReservationError>` | `Ok` containing `2` and the same two ordered events | `Error(OutOfStock)`; no writer or event list |

Use the first shape for a trace of an attempt; use the second for a proposal the
caller should receive only on success. A success-only caller can project a trace
with `trace.Value.Map(value => trace.Map(_ => value))`. This keeps the original
events on success and deliberately drops them on error. Once dropped, the trace
cannot be reconstructed from the error alone. Neither shape publishes anything.

### Reader: compose computations with explicit dependencies

```csharp
Reader<IReadOnlyDictionary<AccountNumber, Account>, decimal> TotalBalance() =>
    Reader.Ask<IReadOnlyDictionary<AccountNumber, Account>>()
        .Map((IReadOnlyDictionary<AccountNumber, Account> ledger) =>
            ledger.Values.Sum((Account account) => account.Balance));

Reader<IReadOnlyDictionary<AccountNumber, Account>, decimal> report = TotalBalance();
decimal total = report.Run(Ledger); // 6050m
decimal checkingOnly = report.Run(new Dictionary<AccountNumber, Account>
{
    [Checking.Number] = Checking,
}); // 50m
```

`Reader` defers work until `Run`, uses the same environment throughout `Bind` and `Zip`,
and reruns the computation on each invocation. `Local` adapts an outer environment;
`Reader.Pure<TEnv, T>(value)` ignores it. Delegates and reference values are not cloned,
and neither purity nor thread safety is enforced.

`Reader` and `ReaderResult` execute nested delegates. For an unbounded collection,
iterate inside one computation instead of chaining one `Map` or `Bind` per item;
the latter consumes stack as the chain grows. See the [large-chain measurements](benchmarks/large-chains.md).

`ReaderResult<TEnv, T, TError>` combines this with Derail's short-circuiting `Result`:

```csharp
ReaderResult<IReadOnlyDictionary<AccountNumber, Account>, Writer<Receipt, TransferRecorded>, TransferError>
    AuditedTransfer(string fromInput, string toInput, decimal amount) =>
    new((IReadOnlyDictionary<AccountNumber, Account> ledger) => Transfer(fromInput, toInput, amount, ledger)
        .Map((Receipt receipt) => new Writer<Receipt, TransferRecorded>(receipt,
            [new TransferRecorded(receipt.From, receipt.To, receipt.Amount)])));

ReaderResult<IReadOnlyDictionary<AccountNumber, Account>, Writer<Receipt, TransferRecorded>, TransferError> workflow =
    AuditedTransfer("1234.56.78901", "1098.76.54321", 200m);
Result<Writer<Receipt, TransferRecorded>, TransferError> result = workflow.Run(Ledger); // Ok: receipt and audit event
```

It supports `Map`, `MapError`, `Bind`, `Recover`, `Zip`, `Local`, and LINQ.
`Zip` never runs the right computation after a left error. `Recover` handles an error
with another computation using the same environment. `ReaderResult.FromReader<TEnv, T, TError>`
lifts an infallible reader, and `ToReader()` exposes the underlying result-valued reader.
The banking example overloads `Transfer` to accept the supplied ledger and threads it through
account lookups. Running the same workflow with only 100m in savings returns `InsufficientFunds`
instead of a Writer. See [`BankingExamples.cs`](tests/Derail.Tests/BankingExamples.cs) for the
executable examples and [`PrimitiveExamples.cs`](tests/Derail.Tests/PrimitiveExamples.cs) for a pricing example.

`Reader`, `Writer`, and `NonEmpty` are sealed classes, not unions: they wrap a computation,
pair a value with events, and require a head element respectively. Their constructors and
immutable collection storage protect these invariants; none needs alternative union cases.

### Async

#### Asynchronous ReaderResult

Build an `AsyncReaderResult` once and run it with the environment and token for
each request. For example, with an open database connection:

```csharp
var lookup = OrderLookup.Status(orderId); // No query yet.
Result<string, LookupError> status = await lookup.Run(connection, cancellationToken);
```

[`OrderLookup.cs`](tests/Derail.Tests/Examples/OrderLookup.cs) validates the ID before
issuing a parameterized query. A missing row returns `NotFound`; database exceptions
and cancellation propagate to the caller. `Recover` handles only typed errors.
`Bind`, `Recover`, and sequential `Zip` share the environment and token. `Local`
adapts a larger environment; `Map`, `MapError`, `Ensure`, and LINQ compose without
executing. Lift existing values with `FromResult` and computations with `FromReaderResult`.

`Run` returns `Task<Result<T, TError>>`, matching common I/O APIs such as
`ExecuteScalarAsync`. `ValueTask` can save allocations for frequently synchronous
operations, but has single-consumption constraints; there is one `Task`-based
workflow API. A pre-canceled token prevents execution. Once I/O starts, cancellation
is cooperative: forward the supplied token to the underlying operation.

#### Bringing existing values into a reader workflow

| Starting point | Conversion | What happens at `Run` |
| --- | --- | --- |
| An already computed `Result<T, E>` | `ReaderResult.FromResult<TEnv, T, E>(result)` | Returns the same success or error in every environment; the original operation is not repeated. |
| An environment lookup returning `Option<T>` | `ReaderResult.FromLookup(lookup, whenNone)` | Calls the lookup once per run. `Some(null)` is a success; only `None` calls the error factory. |
| An infallible `Reader` needed in an async fallible workflow | `AsyncReaderResult.FromReaderResult(ReaderResult.FromReader<TEnv, T, E>(reader))` | Runs the synchronous reader with the supplied environment; lifting it does not schedule background work. |
| An existing `Writer<T, E>` needed in an ordinary reader | `Reader.Pure<TEnv, Writer<T, E>>(writer)` | Returns the writer itself, with its ordered events intact. Lifting only `writer.Value` discards those events. |

The [executable shipping quote](tests/Derail.Tests/Examples/ShippingQuote.cs) combines an
already priced subtotal with a carrier rate looked up at `Run`. It keeps the
original error or the writer's ordered events. In contrast, `Result.ToOption()`
explicitly discards the error: converting back can supply a new error, not restore the old one.

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
A flattened `Reader` runs both layers with the same environment at `Run`, and a
flattened `Writer` keeps the inner value with outer events before inner events.

### Collecting a sequence of values

Use `Sequence` when each element is already an `Option`, `Result`, or `Validation`.
Use `Traverse` when a function produces one for each input:

```csharp
Result<IReadOnlyList<int>, string> quantities = Result.Traverse(inputs, ParseQuantity);
```

Both collect immutable snapshots in input order. Empty input succeeds with an empty
list. `Option` stops reading on `None`; `Result` stops on the first `Error`.
`Validation` reads every element and collects all errors in encounter order.

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

The [order shipment API](examples/Orders/README.md) shows how the primitives work
together in ASP.NET Core, with PostgreSQL transactions and HTTP integration tests.

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
src/Derail/                  Library: Option, Result, Validation, NonEmpty, Writer, Reader, async extensions
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
