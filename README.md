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

var welcome = ValidateApplication(owner, number, deposit)
    .ToResult()
    .Map(a => $"Welcome, {a.Owner}");
```

### NonEmpty: require at least one item

```csharp
var batch = NonEmpty.FromEnumerable(new[] { 1, 2, 3 })
    .ToResult(() => "At least one item is required");

var total = batch.Map(items => items.Map(x => x * 2).Sum()); // Ok(12)
var required = new NonEmpty<int>(1, 2, 3);
```

`NonEmpty<T>` implements `IReadOnlyList<T>` with `Head`, `Tail`, and `Count`.
Construction snapshots the input; `Map` and `Append` preserve order and non-emptiness.
It is a sealed class: `default` is null, not a constructed empty collection.
Ordinary LINQ operations such as `Where` still return ordinary sequences that may be empty.

### Writer: return events instead of emitting them

```csharp
var workflow =
    from first in new Writer<int, string>(2, ["first"])
    from second in new Writer<int, string>(3, ["second"])
    select first + second;

var (value, events) = workflow; // 5, ["first", "second"]
```

Writer composition is eager, but events are only data: nothing is logged or published.
`Map` preserves events; `Bind` and `Zip` append them left-to-right; `MapEvents` transforms them.
`Writer.Pure<T, TEvent>(value)` starts without events, and `Writer.Tell(event)` records
one event with a `ValueTuple` result. Event inputs are snapshotted, and callers own delivery policy.

### Reader: compose computations with explicit dependencies

```csharp
var reader =
    from first in Reader.Ask<int>()
    from second in new Reader<int, int>(env => env * 2)
    select first + second;

var value = reader.Local<string>(text => text.Length).Run("test"); // 12
```

`Reader` defers work until `Run`, uses the same environment throughout `Bind` and `Zip`,
and reruns the computation on each invocation. `Local` adapts an outer environment;
`Reader.Pure<TEnv, T>(value)` ignores it. Delegates and reference values are not cloned,
and neither purity nor thread safety is enforced.

`ReaderResult<TEnv, T, TError>` combines this with Derail's short-circuiting `Result`:

```csharp
var checkedReader = new ReaderResult<int, int, string>(quantity => quantity > 0
    ? Result.Ok<int, string>(quantity)
    : Result.Error<int, string>("Quantity must be positive"));

var doubled = checkedReader.Map(quantity => quantity * 2).Run(3); // Ok(6)
```

It supports `Map`, `MapError`, `Bind`, `Recover`, `Zip`, `Local`, and LINQ.
`Zip` never runs the right computation after a left error. `Recover` handles an error
with another computation using the same environment. `ReaderResult.FromReader<TEnv, T, TError>`
lifts an infallible reader, and `ToReader()` exposes the underlying result-valued reader.
See [`PrimitiveExamples.cs`](tests/Derail.Tests/PrimitiveExamples.cs) for an example
combining a fallible Reader with Writer events. Validation's existing error API is unchanged.

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

Push a tag like `v0.2.0`. The [release workflow](.github/workflows/release.yml) builds, tests, packs with that version, publishes to nuget.org through trusted publishing, and creates a GitHub release.

## License

[MIT](LICENSE)
