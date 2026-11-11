# Derail

[![CI](https://github.com/Frixxie/derail/actions/workflows/ci.yml/badge.svg)](https://github.com/Frixxie/derail/actions/workflows/ci.yml)

Railway-oriented programming for C#, built on the native `union` types in C# 15 / .NET 11.

- **`Option<T>`** — a value that may be absent: `Some<T>` or `None`.
- **`Result<T, TError>`** — an operation that may fail: `Ok<T>` or `Error<TError>`. Stops at the first error.
- **`Validation<T, TError>`** — independent checks: `Valid<T>` or `Invalid<TError>`. Collects every error.

Because the cases are real union members, you get exhaustive `switch` expressions and implicit conversions from each case for free.

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
dotnet test tests/Derail.Tests
dotnet run -c Release --project benchmarks/Derail.Benchmarks
```

## Layout

```
src/Derail/                  Library: Option, Result, Validation, async extensions
tests/Derail.Tests/          Unit, property-based and example tests
benchmarks/Derail.Benchmarks BenchmarkDotNet benchmarks
```

## Releasing

Push a tag like `v0.2.0`. The [release workflow](.github/workflows/release.yml) builds, tests, packs with that version, publishes to nuget.org through trusted publishing, and creates a GitHub release.

## License

[MIT](LICENSE)
