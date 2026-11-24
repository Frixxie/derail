# Changelog

## Unreleased

- **Breaking:** Removed `Reader`, `ReaderResult`, `AsyncReaderResult`, and `Writer`.
  Pass dependencies as ordinary parameters and compose `Task<Result<T, TError>>`
  pipelines with `BindAsync`, `MapAsync`, and `RecoverAsync`; return events as
  plain data alongside the value. The orders example shows both approaches.

- Added `Validation.AndThen` for checks that depend on an earlier valid value. It
  short-circuits like `Result.Bind` and is deliberately not named `Bind`.

- **Breaking:** `Result<T, NonEmpty<TError>>.ToValidation()` now returns
  `Validation<T, TError>` with the original errors, reversing `Validation.ToResult()`.
  Previously it returned `Validation<T, NonEmpty<TError>>` with one wrapped error.

- Three- and four-way `Validation.Zip` copy errors once instead of once per pairwise step.

- Property tests now use generated functions for the `Result` monad laws and the
  `Validation` applicative laws, and check the monad laws for `BindAsync` on
  `Task` and `ValueTask`.

## 0.8.0

- **Breaking:** `Option<T>` is a native union again. `default(Option<T>)` matches neither case;
  combinators that inspect it throw `SwitchExpressionException`. Use `Option.None` for absence.
  The NuGet package includes analyzer error `DRL001` for explicit `default` and
  parameterless construction of an `Option`, `Result` or `Validation`. The analyzer can't
  see unassigned fields, array slots, generic `default(T)` or serializer output; see
  "Default initialization" in the README.
  This reverses the 0.7.0 behavior on purpose: an option you forgot to initialize now fails
  loudly instead of silently meaning absent, as `default` already does for `Result` and `Validation`.

- `Validation.Sequence` collects errors directly from their non-empty storage,
  avoiding one enumerator allocation per invalid input while preserving error order.

- Added `ReaderResult.FromResult` for computed outcomes and `ReaderResult.FromLookup`
  for optional environment lookups with a lazy missing-value error.

- `Validation.Zip` now combines four independent inputs and collects their errors left to right.

- Added an ASP.NET Core order-shipment example with transactional PostgreSQL persistence and HTTP integration tests.

- Added `GetValueOrElse` to `Option`, `Result`, and `Validation` for computing a fallback value only when needed.

- Added `Flatten()` for nested `Option`, `Result`, `Reader`, and `Writer` values.

- Added `AsyncReaderResult<TEnv, T, TError>` for lazy, cancellable workflows
  with explicit dependencies, typed errors, and lifts from `Result` and `ReaderResult`.

- Added `Option.Sequence`, `Option.Traverse`, `Result.Sequence`, `Result.Traverse`,
  and `Validation.Traverse` for ordered, single-pass collection processing.

- Added `Result.Recover`, `Result.TapError`, and `Option.OrElse` for handling typed
  errors and missing values, with `Task` and `ValueTask` pipeline support.

- **Behavior change:** `NonEmpty<T>` now compares and hashes by ordered elements.
  Distinct collections with equal elements represent the same dictionary or set
  key. For identity comparisons use `ReferenceEquals`; for identity-based keys
  pass `ReferenceEqualityComparer.Instance` to the dictionary or set constructor.

- **Breaking:** `NonEmpty<T>`'s indexer throws `ArgumentOutOfRangeException` instead of
  `IndexOutOfRangeException` for an index outside the collection.

- `Validation.Traverse` has an overload for `NonEmpty<T>` that returns `NonEmpty` values.
  Calls that passed a `NonEmpty<T>` and stored the result as `IReadOnlyList<T>` now need
  `.Map(values => (IReadOnlyList<T>)values)` or an `IEnumerable<T>`-typed argument.

- Added `AsyncReaderResult.FromLookup` for async optional lookups, `AsyncReaderResult.FromTask`
  for steps without a domain error, and `Tap` and `TapError` on `AsyncReaderResult`.

- **Breaking:** `Task` combinators now follow the same await policy as `ValueTask` combinators.
  Callbacks after pending inputs no longer request the caller's synchronization
  context. If a callback updates a UI, dispatch that update to the UI thread.

## 0.7.0

### Behavior changes

- `default(Option<T>)` now behaves as `None` in pattern matching, equality, and all combinators. Previously it matched neither case.
- `Validation.Zip` now rejects an uninitialized input even when the other input is `Invalid`, consistent with `Validation.Sequence`.

## 0.6.0

### Added

- `Result` and `Option` pipelines now accept `ValueTask`-returning callbacks in `MapAsync` and `BindAsync`. `Result` also accepts them in `TapAsync` and `EnsureAsync`.
- Chain operations directly on `ValueTask<Result<T, TError>>` and `ValueTask<Option<T>>` without converting to `Task`.

## 0.5.0

### Added

- Added `EnsureAsync` for async predicates and `TapAsync` for awaited observations on both `Result<T, TError>` and `Task<Result<T, TError>>`.

## 0.4.0

### Added

- `Result.Tap` observes a successful value without changing the result. `Result.Ensure` checks a successful value and creates an error only if the predicate fails.

### Improvements

- `Writer.Bind` (and `Zip`/LINQ `from` clauses) now appends events in constant time
  and flattens them when the events are read. Chaining 1,024 binds went from
  95 µs / 2.3 MB to 20 µs / 221 KB. Event order and the public API are unchanged.

## 0.3.0

### Breaking changes

- `Invalid<TError>` is now a sealed class backed by `NonEmpty<TError>`. Every
  constructed invalid case contains at least one error; `default(Invalid<TError>)`
  is null rather than an empty failure.
- `Invalid<TError>.Errors`, its deconstruction, and the invalid callback of
  `Validation.Match` now expose `NonEmpty<TError>` rather than `IReadOnlyList<TError>`.
- `Validation.ToResult()` now returns `Result<T, NonEmpty<TError>>`. Update explicit
  result types and callback signatures to use the non-empty error type.

Keep constructing failures with `Validation.Invalid<T, TError>(firstError, ...)`
or `new Invalid<TError>(firstError, ...)`. Use `Errors.Head` for the required first
error and `Errors.Tail` for any remaining errors. `NonEmpty<TError>` still implements
`IReadOnlyList<TError>` for consumers that only need collection access.

### Improvements

- Error mapping, accumulation, sequencing, and result conversion preserve
  non-emptiness, encounter order, and structural equality of invalid cases.
- Expanded executable banking examples cover non-empty payment batches,
  ledger-dependent Readers, Writer audit events, and non-empty validation errors.
- Added regression and property-based tests for error invariants and updated the
  README with usage and migration examples.

The library still targets .NET 11 / C# 15. Transfers in the banking examples remain
pure proposals: they do not persist balances or publish events.
