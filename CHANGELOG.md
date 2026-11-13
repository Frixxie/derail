# Changelog

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
