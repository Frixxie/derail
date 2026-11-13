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

```csharp
using Derail;
```

## Quick tour

The examples below come from [`BankingExamples.cs`](tests/Derail.Tests/BankingExamples.cs), where they run as tests.

### Option: values that may be missing

```csharp
Option<Account> FindAccount(AccountNumber number) =>
    ledger.TryGetValue(number, out var account) ? Option.Some(account) : Option.None;

var displayName = Option.FromNullable(account.Nickname).GetValueOrDefault(account.Number.Value);

var premium = FindAccount(number).Where(a => a.Balance >= 500m);

var total = FindAccount(savings).Zip(FindAccount(checking), (a, b) => a.Balance + b.Balance);
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

Linear pipelines read well with `Bind` and `Map`:

```csharp
var balance = ParseAccountNumber("1234.56.78901")
    .Bind(GetAccount)
    .Bind(EnsureActive)
    .Bind(a => Withdraw(a, 100m))
    .Map(a => a.Balance);
```

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
var message = Transfer(from, to, 200m) switch
{
    Ok<Receipt>(var r) => $"Moved {r.Amount:0.00} from {r.From} to {r.To}",
    Error<TransferError>(TransferError.InsufficientFunds(var n, var balance, var requested)) =>
        $"Account {n} has {balance:0.00}, needs {requested:0.00}",
    // ...
};

var status = Transfer(from, to, amount)
    .MapError(error => error switch
    {
        TransferError.AccountNotFound => 404,
        TransferError.AccountFrozen => 423,
        TransferError.InsufficientFunds => 409,
        _ => 400,
    })
    .Match(_ => 200, code => code);
```

### Validation: report every problem at once

```csharp
Validation<AccountApplication, string> ValidateApplication(string owner, string number, decimal deposit) =>
    ValidateOwner(owner).Zip(ValidateNumber(number), ValidateDeposit(deposit),
        (o, n, d) => new AccountApplication(o, n, d));

ValidateApplication("", "12", 10m);
// Invalid: "Owner is required", "'12' is not an account number",
//          "Initial deposit must be at least 100.00, was 10.00"
```

`Validation.Sequence` checks a whole batch, and `ToResult()` hands a validated value back to a `Result` pipeline:

```csharp
var numbers = Validation.Sequence(batch.Select(ValidateNumber));

Result<AccountApplication, NonEmpty<string>> application =
    ValidateApplication("", "12", 500m).ToResult();

var message = application
    .Map(a => $"Welcome, {a.Owner}")
    .Match(ok => ok, errors => string.Join("; ", errors));
// "Owner is required; '12' is not an account number"
```

Failures always have a first error, so an error summary needs no empty-list fallback:

```csharp
var summary = ValidateApplication("", "12", 10m).Match(
    _ => "Application accepted",
    errors => $"{errors.Head} ({errors.Tail.Count} more problems)");
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

var batch = RequirePayments([
    new Payment(Checking.Number, 200m),
    new Payment(Seized.Number, 50m),
]);

var total = batch.Map(payments => payments.Map(payment => payment.Amount).Sum()); // Ok(250m)
var empty = RequirePayments([]); // Error("At least one payment is required")
```

`NonEmpty<T>` implements `IReadOnlyList<T>` with `Head`, `Tail`, and `Count`.
Construction snapshots the input; `Map` and `Append` preserve order and non-emptiness.
It is a sealed class: `default` is null, not a constructed empty collection.
Ordinary LINQ operations such as `Where` still return ordinary sequences that may be empty.
Non-emptiness alone does not validate payment amounts or destination accounts.

### Writer: return events instead of emitting them

```csharp
sealed record TransferRecorded(AccountNumber From, AccountNumber To, decimal Amount);

var result = Transfer("1234.56.78901", "1098.76.54321", 200m)
    .Map(receipt => new Writer<Receipt, TransferRecorded>(receipt,
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
        .Map(ledger => ledger.Values.Sum(account => account.Balance));

var report = TotalBalance();
var total = report.Run(Ledger); // 6050m
var checkingOnly = report.Run(new Dictionary<AccountNumber, Account>
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
    new(ledger => Transfer(fromInput, toInput, amount, ledger)
        .Map(receipt => new Writer<Receipt, TransferRecorded>(receipt,
            [new TransferRecorded(receipt.From, receipt.To, receipt.Amount)])));

var workflow = AuditedTransfer("1234.56.78901", "1098.76.54321", 200m);
var result = workflow.Run(Ledger); // Ok: receipt (800m, 250m) and its audit event
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

`MapAsync`/`BindAsync` lift async functions, and the same combinators work directly on `Task<Result<…>>` and `Task<Option<…>>`, so async pipelines read like sync ones:

```csharp
Task<Result<decimal, TransferError>> BalanceInAsync(string input, string currency) =>
    ParseAccountNumber(input)
        .BindAsync(n => FindAccountAsync(n).ToResult(TransferError () => new TransferError.AccountNotFound(n)))
        .Bind(EnsureActive)
        .MapAsync(async a => a.Balance * await ExchangeRateAsync(currency));
```

## API overview

| | `Option<T>` | `Result<T, TError>` | `Validation<T, TError>` |
| --- | --- | --- | --- |
| Create | `Option.Some`, `Option.None`, `Option.FromNullable` | `Result.Ok`, `Result.Error` | `Validation.Valid`, `Validation.Invalid` |
| Inspect | `IsSome`, `IsNone` | `IsOk`, `IsError` | `IsValid`, `IsInvalid` |
| Transform | `Map`, `Where` | `Map`, `MapError` | `Map`, `MapErrors` |
| Chain | `Bind`, LINQ | `Bind`, LINQ | — (use `ToResult`) |
| Combine | `Zip`, `Apply` | `Zip`, `Apply` (first error) | `Zip` (2 or 3), `Apply`, `Sequence` (all errors) |
| Exit | `Match`, `GetValueOrDefault` | `Match`, `GetValueOrDefault` | `Match`, `GetValueOrDefault` |
| Convert | `ToResult`, `ToValidation` | `ToOption`, `ToValidation` | `ToResult` |
| Async | `MapAsync`, `BindAsync`, `Task<Option<T>>` extensions | `MapAsync`, `BindAsync`, `Task<Result<T, TError>>` extensions | — |

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
