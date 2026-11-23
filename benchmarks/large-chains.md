# Large composition chains

Measured on 2026-11-24, Apple M5 Pro, macOS 26.5.1, Arm64 RyuJIT, .NET SDK
11.0.100 / runtime 11.0.0. BenchmarkDotNet 0.16.0-preview.2 ShortRun: one launch,
three warmups and three measured iterations, targeting 100 ms. Treat timings as
exploratory. Baseline library: `28e72d8e` (before the validation copy change).

## Readers: construction, execution and stack depth

These are prebuilt pipelines run once per operation. Each cell is **mean µs / B allocated**.

| Operation | 1,024 steps | 4,096 steps |
| --- | ---: | ---: |
| Direct loop | 0.23 / 0 | 0.92 / 0 |
| Nested delegates | 3.53 / 0 | 17.48 / 0 |
| Reader.Map | 5.81 / 0 | 23.66 / 0 |
| Reader.Bind | 17.08 / 106,496 | 80.91 / 425,984 |
| ReaderResult.Map | 10.09 / 24,600 | 44.56 / 98,328 |
| ReaderResult.Bind, success | 28.80 / 221,208 | 164.90 / 884,760 |
| ReaderResult.Bind, initial error | 17.67 / 114,712 | 88.13 / 458,776 |

Construction is separate: building 4,096 steps took 46.44 µs / 448 KiB for
Reader.Map, 46.53 µs / 448 KiB for Reader.Bind, and 59.96 µs / 544 KiB for
ReaderResult.Map. A failed bind chain skips user continuations but must still
descend to the initial result and unwind its nested wrappers. Successful Bind's
allocations include the `Pure` reader constructed by the continuation at each step.

**These reader chains are not stack-safe at arbitrary depth.** A separate process
per shape sampled the CLR stack reserve inside the innermost delegate, every 64
steps, on a thread requesting a 1 MiB stack. The first low-reserve samples were
9,728 (Reader.Map), 8,320 (Reader.Bind), 7,296 (ReaderResult.Map) and 6,464
(ReaderResult.Bind). These are warnings, **not** crash thresholds or supported
maximums. Thread stack size, callbacks and JIT tiering change the boundary. For
unbounded input, iterate inside a reader rather than building one reader per item.

## NonEmpty and Writer

| Operation | 1,024 elements | 4,096 elements |
| --- | ---: | ---: |
| NonEmpty: append singletons | 76.07 µs / 2.05 MiB | 1,139.28 µs / 32.22 MiB |
| NonEmpty: FromEnumerable (array) | 0.15 µs / 4.08 KiB | 0.52 µs / 16.08 KiB |
| Writer: build single-event Bind chain | 18.89 µs / 216 KiB | 93.18 µs / 864 KiB |
| Writer: first read, including flattening | 9.75 µs / 20,808 B | 34.81 µs / 82,296 B |
| Writer: cached last-event lookup | 1.77 ns / 0 B | 1.57 ns / 0 B |

Quadrupling singleton appends allocated about 16× more: every append copies the
accumulated prefix. Use `FromEnumerable` when the batch is available. Writer
construction and first event access scaled roughly with event count. The first-read
benchmark gives each of 32 readers its own unflattened cache over the same immutable
tree before each measured iteration; it does not charge tree construction to reads.
The cached benchmark indexes `Events` immediately rather than returning the list.
`WriterTests.Concurrent_first_reads_return_complete_ordered_snapshots` starts four
readers together on one unread writer and compares each complete sequence to the
expected order. It does not require competing threads to share a single allocation.

## Validation.Sequence: before and after

Each invalid input contributes one prebuilt error. Cells are **mean µs / allocated bytes**.

| Count | Batch | Before | After |
| ---: | --- | ---: | ---: |
| 1,024 | All valid | 1.61 / 8,464 | 2.04 / 8,464 |
| 1,024 | Alternating | 4.48 / 31,088 | 1.91 / 6,512 |
| 1,024 | All invalid | 8.14 / 61,832 | 2.98 / 12,680 |
| 4,096 | All valid | 6.42 / 33,088 | 8.17 / 33,088 |
| 4,096 | Alternating | 17.62 / 123,296 | 7.72 / 24,992 |
| 4,096 | All invalid | 32.26 / 246,200 | 11.73 / 49,592 |

Copying each `NonEmpty` head and immutable tail directly into the batch builder
saves **48 B per invalid input** versus enumerating the collection. Mixed and
all-invalid batches improved in this run. All-valid allocation was unchanged,
but its measured latency increased; this is not a universal speedup. Error order,
duplicates and null payloads are preserved.

## Reproduce

```sh
dotnet build -c Release
for shape in map bind result-map result-bind; do
  dotnet benchmarks/Derail.Benchmarks/bin/Release/net11.0/Derail.Benchmarks.dll --stack-probe "$shape"
done
dotnet run -c Release --no-build --project benchmarks/Derail.Benchmarks -- \
  --filter '*Reader*Benchmarks*1024*' '*Reader*Benchmarks*4096*' \
    '*AppendSingletons*1024*' '*AppendSingletons*4096*' '*FromEnumerable*1024*' '*FromEnumerable*4096*' \
    '*WriterBenchmarks.Bind*1024*' '*WriterBenchmarks.Bind*4096*' '*WriterReadBenchmarks*' \
    '*ValidationSequenceBenchmarks*' --job Short --iterationTime 100 --artifacts BenchmarkDotNet.Artifacts/large-chains
```

The investigation ran 66 cases before the validation optimization and repeated
the 30 `*ValidationSequenceBenchmarks*` cases after it. For a before/after repeat
with this expanded benchmark suite, use the baseline `Validation.Sequence` loop
(`errors.AddRange(invalid.Errors)`) in the before checkout. Full precision results
and timing confidence intervals are in the generated `results/` reports.
