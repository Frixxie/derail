# Benchmarks

Run from the repository root with the .NET 11 SDK, either locally or inside the
[dev container](../.devcontainer/devcontainer.json):

```sh
# Validate every workload and its output checks; not a performance measurement.
dotnet run -c Release --project benchmarks/Derail.Benchmarks -- --filter '*' --job Dry

# Exploratory measurements for a single feature.
dotnet run -c Release --project benchmarks/Derail.Benchmarks -- --filter '*WriterBenchmarks*' --job Short

# Default BenchmarkDotNet measurement job for more reliable comparisons.
dotnet run -c Release --project benchmarks/Derail.Benchmarks -- --filter '*ReaderExecutionBenchmarks*'
```

BenchmarkDotNet writes reports under `BenchmarkDotNet.Artifacts/` (ignored by version
control). `MemoryDiagnoser` reports managed allocations alongside elapsed time.
Every class checks equivalent values, errors, or event order in `GlobalSetup` before
timing. Fixtures are synthetic and created outside the timed methods.

## Workloads

| Class | Measures | Baseline / comparison |
| --- | --- | --- |
| `ResultBenchmarks` | Parsing, positivity check, and mapping; valid, malformed, zero, negative inputs | Equivalent `TryParse` control flow |
| `NonEmptyMapBenchmarks` | Mapping 1, 8, 32, 1024 elements | An array loop producing the same elements |
| `NonEmptyConstructionBenchmarks` | Head/tail construction, sequence conversion, repeated singleton append | Direct head/tail construction |
| `WriterBenchmarks` | Repeated single-event `Bind` versus bulk construction | A local list accumulator, snapshotted into a Writer |
| `ReaderConstructionBenchmarks` | Building map/bind/result pipelines of 1, 8, 32 steps | Equivalent nested delegates |
| `ReaderExecutionBenchmarks` | Repeated execution of prebuilt pipelines | Direct loop; nested delegates also measured |
| `ReaderResultShortCircuitBenchmarks` | Prebuilt bind chains, either successful or failing before the first continuation | Direct short-circuiting control flow |
| `ValidationSequenceBenchmarks` | All-valid, all-invalid, alternating batches of 1, 8, 32, 1024 | Local list accumulation with equivalent ordered results |

## Interpretation

- Construction and repeated execution are separate Reader benchmarks. `Bind`
  creates the next reader during execution; those allocations deliberately remain
  part of the execution measurement.
- Baselines express equivalent outputs, not equivalent abstractions or safety
  guarantees. The JIT may optimize a direct loop more aggressively than delegates.
- NonEmpty arrays and singleton fixtures are prepared in setup. The singleton
  append case therefore measures concatenation, not singleton creation; at count
  one it simply returns the existing singleton.
  `SnapshotThenConstruct` retains the original conversion algorithm as a local
  comparison, so conversion changes can be checked for both allocation and latency.
- Writer `Bulk` receives precomputed events and isolates snapshot cost. Unlike
  `Direct` and `Bind`, it does not compute events inside the timed method. There
  is no event delivery, logging, or I/O in these workloads.
- Validation errors are integers to isolate accumulation rather than string
  allocation. `FailureEvery = 0` means all valid, `1` all invalid, and `2`
  alternating (starting with an error).
- Dry jobs include cold startup/JIT effects: use them only as smoke tests.
  Short jobs are exploratory, not stable regression thresholds.
- Container results depend on CPU scheduling, resource limits, runtime version,
  and host load. Compare before/after runs on the same machine and configuration;
  do not treat these numbers as universal operation costs.

## Improvement candidates

These were the hypotheses at the initial benchmark revision (`2064b105`), before
the optimization work below:

1. **Writer event accumulation:** repeated immutable-array concatenation copies
   the accumulated prefix on every bind, giving quadratic total copying for a
   long chain of single-event writers. Consider a bulk collection operation first;
   persistent/chunked storage is a larger change with read/flattening tradeoffs.
2. **Validation.Sequence:** repeated error concatenation has the same quadratic
   pattern. A method-local builder can collect a batch once, freezing its output
   at the boundary while preserving all errors and their order. Success currently
   builds an immutable list incrementally as well.
3. **NonEmpty mapping/conversion:** `Map` materializes a tail before the public
   constructor snapshots it again, and `FromEnumerable` materializes the whole
   input before copying its tail. A private trusted immutable-storage constructor
   could remove the extra copy without weakening public snapshot guarantees.
   Repeated `Append` also copies prefixes; construct bulk batches when possible.
4. **Reader composition:** wrappers and captured delegates allocate at construction;
   `Bind` and result composition can allocate again during each run. Measure the
   delegate baseline before considering specialized fast paths. Avoid a generic
   effect hierarchy or public representation changes just to optimize tiny loops.

Prioritize measured allocation/scaling problems. Preserve immutability, laziness,
short-circuiting, and event/error order with regression and property tests before
changing implementations.

## Initial exploratory results (2026-11-13)

All 107 Dry cases completed successfully. A separate ShortRun measured 12 focused
cases on an AMD Ryzen 7 7735U, Ubuntu 26.04 container, .NET SDK 11.0.100 / runtime
11.0.0, workstation GC. ShortRun used one launch, three warmups, and three measured
iterations. High process priority was unavailable in the non-root container.

| Workload | Library allocation per operation | Comparison |
| --- | ---: | ---: |
| Writer: 1024 single-event binds | 2244 KiB | Local list then Writer: 8.11 KiB |
| Validation.Sequence: 1024 failures | 2211.91 KiB | Local list accumulation: 40.33 KiB |
| NonEmpty.Map: 1024 elements | 8.13 KiB | Array loop: 4.02 KiB |
| Reader.Map: 32 prebuilt steps | 0 B | Nested delegates: 0 B |
| Reader.Bind: 32 prebuilt steps | 3328 B | Direct loop: 0 B |
| ReaderResult.Map: 32 prebuilt steps | 1560 B | Direct loop: 0 B |

The short run's timing confidence intervals were wide, so allocation differences
are the stronger evidence here. These are baseline comparisons, not measured
before/after speedups. Writer's precomputed `Bulk` snapshot used 4.05 KiB, but does
less work than the event-producing baselines and is not a like-for-like speedup.

**Recommended order:** optimize batch validation first (large benefit with a small,
localized implementation change); remove NonEmpty's extra copy next; investigate
Writer accumulation without compromising snapshot semantics; then target
ReaderResult's per-run allocations. Reader.Map already runs allocation-free in
this workload, and Reader.Bind includes allocations inherent to the chosen
continuation (`Reader.Pure` constructs a new reader on each step).

Reproduce the focused run:

```sh
dotnet run -c Release --project benchmarks/Derail.Benchmarks -- \
  --filter '*WriterBenchmarks*1024*' '*NonEmptyMapBenchmarks*1024*' \
    '*ValidationSequenceBenchmarks*1024*FailureEvery: 1*' '*ReaderExecutionBenchmarks*32*' \
  --job Short --artifacts BenchmarkDotNet.Artifacts/exploratory
```

The full generated reports remain in the local artifacts directory, outside version
control. No library implementations were changed in the initial benchmark revision.

## Optimization approach

- `Validation.Sequence` uses method-local immutable-array builders rather than
  repeatedly concatenating accumulated results. It enumerates once, continues after
  errors, rejects an uninitialized union, and returns immutable snapshots.
  `Invalid<TError>` now stores `NonEmpty<TError>` and is a sealed class: a constructed
  failure cannot have zero errors. Sequence collects these errors into a builder,
  then snapshots the required head and tail. The measurements below predate this
  representation change and should be rerun before drawing allocation conclusions.
- `NonEmpty` has a private immutable-storage constructor. `Map` allocates a tail
  of the exact size through `ImmutableArray.CreateRange` without another copy.
  Conversion bulk-copies array tails; other inputs are enumerated once, reserving
  tail size when the input exposes a count without enumeration.
  `Append` also avoids a redundant snapshot;
  repeated append still has quadratic copying and is not a bulk-building API.
- `Result.Map`, `MapError`, and `GetValueOrDefault` use direct pattern matching
  instead of capturing delegates. This preserves branch behavior while removing
  avoidable closures from synchronous pipelines and ReaderResult mapping.
- Writer storage and Reader representations are unchanged. Local mutation stays
  confined to collection construction; no mutable storage is shared with callers.

### Measured outcome

Same machine/runtime and ShortRun settings as the initial investigation:

| Workload | Before | After |
| --- | ---: | ---: |
| Validation.Sequence, 1024 failures | 2211.91 KiB | 8.33 KiB |
| NonEmpty.Map, 1024 elements | 8.13 KiB | 4.05 KiB |
| ReaderResult.Map, 32 prebuilt steps | 1560 B | 792 B |
| Array conversion, 1024 elements (original strategy versus optimized) | 8.10 KiB | 4.08 KiB |

These are managed allocations per operation, not time ratios. Generated reports
are under `BenchmarkDotNet.Artifacts/optimized`, `optimized-final`, and
`conversion-final`. Successful 1024-item validation batches used 8.27 KiB and
alternating batches used 4.30 KiB. Builder-based batching has fixed overhead, so
very small batches can allocate more than the original fold; the main gain is
linear scaling rather than repeated copying.

Measurement rejected two intermediate implementations: the manual NonEmpty map
builder reduced allocations but slowed the measured transform, and enumerating
arrays element-by-element was slower than the original two-copy conversion.
The final implementation uses the standard immutable-array transform and a
bulk-copy array conversion path instead. Timings remain exploratory because the
ShortRun confidence intervals are wide; no tight latency claims are made here.

All 248 tests and all 111 benchmark smoke cases pass. The new tests pin single-pass
enumeration, snapshot isolation, mapper order, enumerator disposal, default-invalid
behavior, and agreement with a Zip-based reference fold.

To rerun the optimized workloads:

```sh
dotnet run -c Release --project benchmarks/Derail.Benchmarks -- \
  --filter '*NonEmpty*1024*' '*ValidationSequenceBenchmarks*1024*' '*ReaderExecutionBenchmarks*32*' \
  --job Short --artifacts BenchmarkDotNet.Artifacts/optimized
```
