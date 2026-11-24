# Large composition chains

Measured on 2026-11-24, Apple M5 Pro, macOS 26.5.1, Arm64 RyuJIT, .NET SDK
11.0.100 / runtime 11.0.0. BenchmarkDotNet 0.16.0-preview.2 ShortRun: one launch,
three warmups and three measured iterations, targeting 100 ms. Treat timings as
exploratory. Baseline library: `28e72d8e` (before the validation copy change).

## NonEmpty

| Operation | 1,024 elements | 4,096 elements |
| --- | ---: | ---: |
| NonEmpty: append singletons | 76.07 µs / 2.05 MiB | 1,139.28 µs / 32.22 MiB |
| NonEmpty: FromEnumerable (array) | 0.15 µs / 4.08 KiB | 0.52 µs / 16.08 KiB |

Quadrupling singleton appends allocated about 16× more: every append copies the
accumulated prefix. Use `FromEnumerable` when the batch is available.

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
dotnet run -c Release --no-build --project benchmarks/Derail.Benchmarks -- \
  --filter '*AppendSingletons*1024*' '*AppendSingletons*4096*' '*FromEnumerable*1024*' '*FromEnumerable*4096*' \
    '*ValidationSequenceBenchmarks*' --job Short --iterationTime 100 --artifacts BenchmarkDotNet.Artifacts/large-chains
```

The investigation ran 66 cases before the validation optimization and repeated
the 30 `*ValidationSequenceBenchmarks*` cases after it. For a before/after repeat
with this expanded benchmark suite, use the baseline `Validation.Sequence` loop
(`errors.AddRange(invalid.Errors)`) in the before checkout. Full precision results
and timing confidence intervals are in the generated `results/` reports.
