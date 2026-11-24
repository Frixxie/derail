# Derail

[![CI](https://github.com/Frixxie/derail/actions/workflows/ci.yml/badge.svg)](https://github.com/Frixxie/derail/actions/workflows/ci.yml)

Small functional primitives for C# 15 on .NET 11. Adopt one type at a time in an existing codebase.

| Type | Use it for | Cases |
| --- | --- | --- |
| `Option<T>` | A value that may be missing | `Some<T>` / `None` |
| `Result<T, TError>` | Steps that may fail; stops at the first error | `Ok<T>` / `Error<TError>` |
| `Validation<T, TError>` | Independent checks; collects every error | `Valid<T>` / `Invalid<TError>` |
| `NonEmpty<T>` | A list with at least one element | `Head` + `Tail` |

```sh
dotnet add package Derail
```

## Quick start

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

Each step runs only if the previous one succeeded. `Match` gets either the final value or the first error.

## Which one do I need?

- **One `if` in one place?** Plain C# is clearer. Reach for Derail when outcomes cross method boundaries or several steps compose.
- **Step B needs the result of step A** → `Result` with `Bind`.
- **Several checks that don't depend on each other, and you want all errors** → `Validation` with `Zip`.
- **Something might not be there, and that's not an error** → `Option`.
- **An empty list would be a bug** → `NonEmpty`.

Use typed errors for outcomes a caller can act on (account not found, insufficient funds). Keep exceptions for bugs and infrastructure failures. Derail never turns exceptions or cancellation into errors.

## Option

```csharp
Option<Account> FindAccount(AccountNumber number) =>
    ledger.TryGetValue(number, out var account) ? Option.Some(account) : Option.None;

string name    = Option.FromNullable(account.Nickname).GetValueOrDefault(account.Number.Value);
var premium    = FindAccount(number).Where(a => a.Balance >= 500m);
var total      = FindAccount(savings).Zip(FindAccount(checking), (a, b) => a.Balance + b.Balance);
var withBackup = FindInCache(number).OrElse(() => FindAccount(number));
```

`Option.Some(null)` is present. Use `Option.FromNullable` when `null` should mean `None`.

## Result

Model errors as a closed hierarchy and let each step return a `Result`:

```csharp
abstract record TransferError
{
    public sealed record AccountNotFound(AccountNumber Number) : TransferError;
    public sealed record AccountFrozen(AccountNumber Number) : TransferError;
    public sealed record InsufficientFunds(decimal Balance, decimal Requested) : TransferError;
}

Result<Account, TransferError> GetAccount(AccountNumber number) =>
    FindAccount(number).ToResult(TransferError () => new TransferError.AccountNotFound(number));

Result<decimal, TransferError> balance = ParseAccountNumber(input)
    .Bind(GetAccount)                                                  // next step returns a Result
    .Ensure(a => !a.Frozen, a => new TransferError.AccountFrozen(a.Number)) // reject on a rule
    .Bind(a => Withdraw(a, 100m))
    .Tap(_ => log.WriteLine("Withdrawal completed"))                   // side effect on success
    .Map(a => a.Balance);                                              // transform the value
```

The same pipeline with every type spelled out, showing what each step receives and returns. In your own code, C# usually infers these:

```csharp
Result<AccountNumber, TransferError> number = ParseAccountNumber(input);
Result<Account, TransferError> account = number.Bind((AccountNumber n) => GetAccount(n));
Result<Account, TransferError> active = account.Ensure(
    (Account a) => !a.Frozen,
    (Account a) => new TransferError.AccountFrozen(a.Number));
Result<Account, TransferError> debited = active.Bind((Account a) => Withdraw(a, 100m));
Result<Account, TransferError> logged = debited.Tap((Account _) => log.WriteLine("Withdrawal completed"));
Result<decimal, TransferError> balance = logged.Map((Account a) => a.Balance);
```

When later steps need several earlier values, use query syntax:

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

Handle the outcome with `Match`, a `switch`, or translate errors with `MapError`:

```csharp
int status = Transfer(from, to, amount)
    .MapError(error => error switch
    {
        TransferError.AccountNotFound => 404,
        TransferError.InsufficientFunds => 409,
        _ => 400,
    })
    .Match(_ => 200, code => code);
```

Use `Recover` to try an alternative after an error, and `TapError` to observe one.

> `a.Zip(GetB(), f)` always calls `GetB()`; only `f` is skipped on error. Use `Bind` if `GetB` must run only after `a` succeeds.

## Validation

```csharp
Validation<AccountApplication, string> ValidateApplication(string owner, string number, decimal deposit) =>
    ValidateOwner(owner).Zip(ValidateNumber(number), ValidateDeposit(deposit),
        (o, n, d) => new AccountApplication(o, n, d));

ValidateApplication("", "12", 10m);
// Invalid: "Owner is required", "'12' is not an account number", "Initial deposit must be at least 100.00"
```

`Zip` takes up to four validations. Errors keep argument order, and the constructor runs only when all are valid. The error list is a `NonEmpty<TError>`, so there is always a first error:

```csharp
string summary = ValidateApplication("", "12", 10m).Match(
    _ => "Application accepted",
    errors => $"{errors.Head} ({errors.Tail.Count} more problems)");
```

- `Validation.Sequence` / `Validation.Traverse` validate a whole batch.
- `AndThen` runs a dependent check, but short-circuits like `Bind`.
- `ToResult()` continues in a `Result` pipeline as `Result<T, NonEmpty<TError>>`. Use `ToValidationFromErrors()` to restore the individual errors in order, or `ToValidation()` to keep the collection as one error.

## NonEmpty

```csharp
Result<NonEmpty<Payment>, string> RequirePayments(IEnumerable<Payment> payments) =>
    NonEmpty.FromEnumerable(payments).ToResult(() => "At least one payment is required");

Result<decimal, string> total = RequirePayments(batch).Map(ps => ps.Map(p => p.Amount).Sum());
```

`NonEmpty<T>` is an immutable `IReadOnlyList<T>` with `Head` and `Tail`. `Map` and `Append` keep it non-empty; ordinary LINQ such as `Where` returns a regular sequence. It compares by elements in order.

## Async

`Option` and `Result` support `Task` and `ValueTask`. Async variants (`MapAsync`, `BindAsync`, `EnsureAsync`, `TapAsync`, `OrElseAsync`, `RecoverAsync`, `TapErrorAsync`) chain directly on a wrapped value:

```csharp
Task<Result<decimal, TransferError>> BalanceInAsync(string input, string currency) =>
    ParseAccountNumber(input)
        .BindAsync(n => FindAccountAsync(n).ToResult(TransferError () => new TransferError.AccountNotFound(n)))
        .EnsureAsync(IsActiveAsync, a => new TransferError.AccountFrozen(a.Number))
        .MapAsync(async a => a.Balance * await ExchangeRateAsync(currency));
```

Derail awaits with `ConfigureAwait(false)`, so callbacks may not run on a UI thread. `Validation` is synchronous.

## Collections and nesting

```csharp
Result<IReadOnlyList<int>, string> quantities = Result.Traverse(inputs, ParseQuantity);
Option<int> three = Option.Some(Option.Some(3)).Flatten();
```

`Sequence` collects values that are already wrapped; `Traverse` maps then collects. `Option` and `Result` stop at the first failure; `Validation` collects all errors.

## Pitfall: never use `default`

`default(Option<T>)`, `default(Result<T, E>)` and `default(Validation<T, E>)` are neither case and throw when used. The bundled analyzer reports `DRL001` for explicit `default` and `new()`. It cannot see these, so initialize them yourself:

- uninitialized fields and new arrays (`Array.Fill(slots, Option.None)`)
- `FirstOrDefault()` / `GetValueOrDefault(key)` — pass `Option.None` as the fallback
- deserialized DTOs — map nullable properties with `Option.FromNullable`

## API at a glance

| | `Option<T>` | `Result<T, TError>` | `Validation<T, TError>` |
| --- | --- | --- | --- |
| Create | `Some`, `None`, `FromNullable` | `Ok`, `Error` | `Valid`, `Invalid` |
| Transform | `Map`, `Where` | `Map`, `MapError` | `Map`, `MapErrors` |
| Chain | `Bind`, LINQ | `Bind`, `Ensure`, LINQ | `AndThen` |
| Side effects | — | `Tap`, `TapError` | — |
| Recover | `OrElse` | `Recover` | — |
| Combine | `Zip`, `Apply` | `Zip`, `Apply` | `Zip`, `Apply` (all errors) |
| Collect | `Sequence`, `Traverse` | `Sequence`, `Traverse` | `Sequence`, `Traverse` |
| Exit | `Match`, `GetValueOrDefault`, `GetValueOrElse` | same | same |
| Convert | `ToResult`, `ToValidation` | `ToOption`, `ToValidation`, `ToValidationFromErrors` | `ToResult` |

## More

- Runnable examples: [`BankingExamples.cs`](tests/Derail.Tests/BankingExamples.cs) and the [orders API](examples/Orders/README.md) (ASP.NET Core + PostgreSQL).
- Upgrading: the API may change on `0.x`; see the [changelog](CHANGELOG.md) for migration notes.
- Benchmarks: [benchmarks/README.md](benchmarks/README.md).

## Development

Requires the .NET 11 SDK from `global.json`, or open the repo in the dev container.

```sh
dotnet build
dotnet test --project tests/Derail.Tests
dotnet test --project tests/Derail.Analyzers.Tests
```

To release, bump the version in `src/Derail/Derail.csproj` and push a matching tag (`v0.9.0`); the [release workflow](.github/workflows/release.yml) publishes to NuGet.

## License

[MIT](LICENSE)
