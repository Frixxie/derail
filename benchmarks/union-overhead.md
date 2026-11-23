# What does a Derail union cost?

Storing an explicit `None` in an `Option<int>` or `Option<string>` field allocated
**24 B** with the native union, up from **0 B** with the previous representation
(`e2b619db`). Storing a `Some`, `Ok`, or `Error` also allocated **24 B** with either
an `int` or an existing `string` payload. Inspecting a prebuilt case with `Match`
or a pattern allocated **0 B**.

## Reproduce

From the repository root:

```sh
dotnet run -c Release --project benchmarks/Derail.Benchmarks -- \
  --filter '*UnionConstructionBenchmarks*' '*UnionOperationBenchmarks*' --job Short --iterationTime 100 \
  --artifacts BenchmarkDotNet.Artifacts/native-union-refresh
```

The 32 construction and operation cases below were measured on 2026-11-24 against
Derail revision `c82ef46d`: Apple M5 Pro, macOS 26.5.1, Arm64 RyuJIT, .NET SDK
11.0.100, runtime 11.0.0. BenchmarkDotNet `0.16.0-preview.2` ran out of process
with `MemoryDiagnoser`: one launch, three warmups and three measured iterations targeting 100 ms each.
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
| Nullable, absent | 0.65 / 0 | 0.65 / 0 |
| Option.Some | 3.19 / 24 | 3.53 / 24 |
| Option.None | 3.19 / 24 | 3.12 / 24 |
| Result.Ok | 3.28 / 24 | 3.86 / 24 |
| Result.Error | 3.17 / 24 | 3.63 / 24 |

## Operations: start from a prebuilt integer case

`Map` and `Bind` increment the successful payload and return a wrapper; `Match`
and the switch expression return the integer directly (or −1 on absence/error).
The nullable operations produce the equivalent optional increment and scalar fallback.

| Operation | Absent / error | Present / success |
| --- | ---: | ---: |
| Nullable increment | 0.59 / 0 | 0.48 / 0 |
| Option.Map | 5.07 / 48 | 5.13 / 48 |
| Result.Map | 2.84 / 24 | 2.71 / 24 |
| Option.Bind | 2.74 / 24 | 2.91 / 24 |
| Result.Bind | 3.79 / 24 | 2.74 / 24 |
| Nullable fallback | 0.45 / 0 | 0.67 / 0 |
| Option.Match | 1.38 / 0 | 1.50 / 0 |
| Option pattern | 1.44 / 0 | 0.69 / 0 |
| Result.Match | 1.45 / 0 | 1.45 / 0 |
| Result pattern | 1.38 / 0 | 0.72 / 0 |

`Option.Map` allocated 48 B on either path, compared with 24 B for `Option.Bind`
and for storing the new case directly. If a workload is sensitive to that extra
allocation, compare the capturing callback in `Option.Map` against a direct branch.

## Whole pipeline: equivalent observable work

All five implementations reject a negative input, double a successful value,
reject zero in the dependent step, then return the incremented value or the
negative length of the original error message. Setup checks the outcomes. Each
column times one fixed path, not an unpredictable mix of branches.

The 15 pipeline cases were measured separately against Derail revision `39b79f19`
on the same machine and runtime with the same ShortRun settings, using OneOf
`3.0.271`, CSharpFunctionalExtensions `3.7.0`, and FluentResults `4.0.0`.
`OneOf<int, string>` uses `MapT0` for the success transform and `Match`
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
