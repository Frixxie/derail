# What does a Derail union cost?

In this run, constructing an escaping `Some`, `Ok`, or `Error` allocated **24 B**
with either an `int` or an existing `string` payload. Constructing `None` and reading
a prebuilt case with `Match` or a pattern allocated **0 B**. This distinguishes
the cost of creating a case from the cost of inspecting it. At the measured revision,
`Result` was a native C# union and `Option` used explicit storage for `None`.
The `Option` figures predate its return to native union notation.

## Reproduce

From the repository root:

```sh
dotnet run -c Release --project benchmarks/Derail.Benchmarks -- \
  --filter '*Union*Benchmarks*' --job Short --iterationTime 100 \
  --artifacts BenchmarkDotNet.Artifacts/union-overhead
```

Measured on 2026-11-24 against Derail library revision `e2b619db`. Apple M5 Pro,
macOS 26.5.1, Arm64 RyuJIT, .NET SDK 11.0.100, runtime 11.0.0, C# 15 / Roslyn
`5.12.0-1.26529.118`. BenchmarkDotNet `0.16.0-preview.2`, CSharpFunctionalExtensions
`3.7.0`, FluentResults `4.0.0`. The original 44 cases ran out of process with `MemoryDiagnoser`:
one launch, three warmups and three measured iterations targeting 100 ms each.
Each cell below is **mean ns / allocated bytes per operation**, rounded. The command
also produces full reports with confidence intervals under `results/` in the artifacts
directory. These are exploratory means, not regression thresholds; for more stable
timings rerun without `--job Short --iterationTime 100` on the same machine.

## Construction: store one value to a field

Writing the wrapper to an instance field makes it escape. String creation is not
timed. The nullable reference baseline stores a plain reference or null; it does
not distinguish `Some(null)` from absence as Derail does.

| Operation | `int` payload | `string` payload |
| --- | ---: | ---: |
| Nullable, present | 0.50 / 0 | 0.89 / 0 |
| Nullable, absent | 0.65 / 0 | 0.68 / 0 |
| Option.Some | 3.16 / 24 | 3.67 / 24 |
| Option.None | 0.65 / 0 | 0.65 / 0 |
| Result.Ok | 3.21 / 24 | 3.63 / 24 |
| Result.Error | 3.18 / 24 | 3.63 / 24 |

## Operations: start from a prebuilt integer case

`Map` and `Bind` increment the successful payload and return a wrapper; `Match`
and the switch expression return the integer directly (or −1 on absence/error).
The nullable operations produce the equivalent optional increment and scalar fallback.

| Operation | Absent / error | Present / success |
| --- | ---: | ---: |
| Nullable increment | 0.45 / 0 | 0.46 / 0 |
| Option.Map | 3.47 / 24 | 5.33 / 48 |
| Result.Map | 2.69 / 24 | 2.69 / 24 |
| Option.Bind | 1.45 / 0 | 2.96 / 24 |
| Result.Bind | 3.83 / 24 | 2.72 / 24 |
| Nullable fallback | 0.66 / 0 | 0.46 / 0 |
| Option.Match | 1.39 / 0 | 1.39 / 0 |
| Option pattern | 1.36 / 0 | 1.37 / 0 |
| Result.Match | 1.41 / 0 | 1.47 / 0 |
| Result pattern | 1.39 / 0 | 0.67 / 0 |

`Option.Map` stands out: it allocates 24 B even on `None`, although constructing
`None` itself did not allocate. The benchmark includes combinator overhead, not
just representation. If a real workload is sensitive to this, compare the current
capturing callback in `Option.Map` against a direct branch before changing storage.

## Whole pipeline: equivalent observable work

All five implementations reject a negative input, double a successful value,
reject zero in the dependent step, then return the incremented value or the
negative length of the original error message. Setup checks the outcomes. Each
column times one fixed path, not an unpredictable mix of branches.

After adding OneOf `3.0.271`, all 15 pipeline cases were rerun together against
Derail revision `39b79f19` on the same machine and runtime with the same ShortRun
settings. `OneOf<int, string>` uses `MapT0` for the success transform and `Match`
for the dependent step, forwarding an existing error. To reproduce just this table:

```sh
dotnet run -c Release --project benchmarks/Derail.Benchmarks -- \
  --filter '*UnionPipelineBenchmarks*' --job Short --iterationTime 100 \
  --artifacts BenchmarkDotNet.Artifacts/union-pipeline-oneof
```

| Implementation | Initial error (−1) | Bind error (0) | Success (7) |
| --- | ---: | ---: | ---: |
| Imperative branches | 0.52 / 0 | 0.47 / 0 | 0.46 / 0 |
| Derail | 9.75 / 72 | 7.22 / 72 | 7.19 / 72 |
| CSharpFunctionalExtensions | 3.80 / 0 | 4.67 / 0 | 4.60 / 0 |
| OneOf | 4.08 / 0 | 3.61 / 0 | 3.31 / 0 |
| FluentResults | 126.47 / 1064 | 125.65 / 1112 | 73.38 / 784 |

The direct branch does not retain a typed result, and FluentResults exposes richer
error objects and collections. This compares the cost of expressing **this workflow**,
including observing the error message, not equivalent library feature sets. The
sub-nanosecond imperative results are particularly sensitive to inlining and harness
overhead; allocation counts are the more useful comparison here.
