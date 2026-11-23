# Derail

[![CI](https://github.com/Frixxie/derail/actions/workflows/ci.yml/badge.svg)](https://github.com/Frixxie/derail/actions/workflows/ci.yml)

Railway-oriented programming for C#, built on the native `union` types in C# 15 / .NET 11.

- **`Option<T>`** — a value that may be absent: `Some<T>` or `None`.
- **`Result<T, TError>`** — an operation that may fail: `Ok<T>` or `Error<TError>`. Stops at the first error.
- **`Validation<T, TError>`** — independent checks: `Valid<T>` or `Invalid<TError>`. Collects every error.
- **`NonEmpty<T>`** — an immutable ordered collection with a required first element.
- **`Writer<T, TEvent>`** — a value paired with ordered events, without emitting them.
- **`Reader<TEnv, T>` / `ReaderResult<TEnv, T, TError>`** — reusable computations with explicit dependencies.

Option, Result, and Validation use real union members, giving you exhaustive `switch` expressions and implicit conversions from each case for free.

## Requirements

.NET SDK 11 (see `global.json`).

## Installation

```sh
dotnet add package Derail
```

## Compatibility and versioning

Patch releases preserve the public API; minor releases add compatible functionality.
Before 1.0, intentional breaks require a minor version bump; from 1.0 onward they
require a major bump. A breaking-change PR must document the affected calls and
their replacements in `CHANGELOG.md` and receive explicit maintainer approval
before merge. If package validation needs an exception, suppress only the approved
difference with a justification; do not skip validation or move the baseline to pass CI.

`dotnet pack src/Derail -c Release -o artifacts` checks binary compatibility
against the published baseline pinned in `Derail.csproj`. Advance that baseline
to the latest stable package after each release. To check source compatibility,
run `bash tests/PackageConsumer/check.sh artifacts/Derail.<version>.nupkg` with
the file just packed. This compiles and runs representative consumer calls;
when adding overloads, include inference-sensitive call sites in that fixture.

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

Use `Result` for a sequence of dependent steps that should stop at the first expected failure. Use `Validation` when checks are independent and the caller needs every error.

For readability, aim for **at most one level of indirection**: the pipeline should tell the story, and opening one named step should reveal its rule. For example, `Bind((Account a) => Withdraw(a, amount))` is useful when `Withdraw` contains the balance check. A helper that just forwards to another helper makes that rule harder to find.

Use `Ensure` to reject a successful value that fails a rule, and `Tap` for a synchronous observation such as recording a metric. An existing error skips both. For an async predicate or observation, use `EnsureAsync` or `TapAsync`. If the step returns its own `Result`, use `Bind` or `BindAsync`.

## Quick tour

The examples below come from [`BankingExamples.cs`](tests/Derail.Tests/BankingExamples.cs), where they run as tests.

The examples often spell out types to show what each step receives and returns. In your own code, C# can usually infer them: `.Map((Account a) => a.Balance)` can be written as `.Map(a => a.Balance)`.

### Default initialization

These rules apply to `default` and to elements of newly allocated arrays:

| Primitive | Meaning of `default` | Inspection and use |
| --- | --- | --- |
| `Option<T>` | `None` | Matches the `None` pattern, compares equal to an explicit `None`, and takes the absent branch of `Match`. |
| `Result<T, TError>` | Uninitialized; neither `Ok` nor `Error` | `IsOk` and `IsError` are both false. Initialize with `Result.Ok` or `Result.Error` before use. |
| `Validation<T, TError>` | Uninitialized; neither `Valid` nor `Invalid` | `IsValid` and `IsInvalid` are both false. Initialize with `Validation.Valid` or `Validation.Invalid` before use. |
| `NonEmpty`, `Reader`, `ReaderResult`, `Writer` | `null` reference | No instance exists. Construct one before calling its members. |

An uninitialized result or validation is a programming error, not a domain error.
Combinators such as `Match`, `Map`, and `GetValueOrDefault` throw
`SwitchExpressionException` when they need to inspect its case. `Result.Zip`
stops at a left-hand `Error`, so it never inspects the right-hand result in that
case. `Validation.Zip` inspects both inputs to accumulate errors and rejects an
uninitialized input on either side. `Validation.Sequence` likewise rejects an
uninitialized element even after an `Invalid`.

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

Derail uses `ConfigureAwait(false)` for its own `Task` and `ValueTask` awaits.
Callbacks can run inline when an input is already complete; after a pending input,
do not assume the callback runs on your UI thread. Your own `await` controls where
your code resumes. Await each `ValueTask` only once.

Faults and cancellation from inputs or callbacks propagate to the caller; neither
becomes `None` or a typed `Error`. To cancel an operation, pass your token to the
underlying async call inside the callback.

## API overview

| | `Option<T>` | `Result<T, TError>` | `Validation<T, TError>` |
| --- | --- | --- | --- |
| Create | `Option.Some`, `Option.None`, `Option.FromNullable` | `Result.Ok`, `Result.Error` | `Validation.Valid`, `Validation.Invalid` |
| Inspect | `IsSome`, `IsNone` | `IsOk`, `IsError`, `Tap` | `IsValid`, `IsInvalid` |
| Transform | `Map`, `Where` | `Map`, `MapError` | `Map`, `MapErrors` |
| Chain | `Bind`, LINQ | `Bind`, `Ensure`, LINQ | — (use `ToResult`) |
| Combine | `Zip`, `Apply` | `Zip`, `Apply` (first error) | `Zip` (2 or 3), `Apply`, `Sequence` (all errors) |
| Exit | `Match`, `GetValueOrDefault` | `Match`, `GetValueOrDefault` | `Match`, `GetValueOrDefault` |
| Convert | `ToResult`, `ToValidation` | `ToOption`, `ToValidation` | `ToResult` |
| Async (`Task` or `ValueTask`) | `MapAsync`, `BindAsync`, wrapped `Option<T>` extensions | `MapAsync`, `BindAsync`, `EnsureAsync`, `TapAsync`, wrapped `Result<T, TError>` extensions | — |

## Build, test and benchmark

```sh
dotnet build
dotnet test --project tests/Derail.Tests
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
tests/Derail.Tests/          Unit, property-based and example tests
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
