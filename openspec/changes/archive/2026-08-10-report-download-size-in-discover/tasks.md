## 1. Quantization projection

- [x] 1.1 Add focused model-layer tests for single-file, directory-sharded, flat split-series, companion, malformed-series, missing-size, and overflow discovery projections.
- [x] 1.2 Add the additive quantization and companion result types while preserving existing discovered candidates.
- [x] 1.3 Implement deterministic grouping, shard-series validation, and all-or-unknown size aggregation from the discovered candidates.

## 2. Discovery interfaces

- [x] 2.1 Add CLI parsing tests for `model discover --model-card`, including JSON output and rejection on unsupported commands or malformed arguments.
- [x] 2.2 Thread an explicit include-card option through discovery and fetch the bounded model card only when requested.
- [x] 2.3 Replace the human candidate table with a compact quantization/files/total comparison plus separately identified companions and diagnostics.
- [x] 2.4 Extend JSON and local API tests to cover the additive grouped projection while proving existing per-candidate evidence remains available.

## 3. Verification

- [x] 3.1 Update operator-facing command documentation and examples for default size comparison and opt-in model-card output.
- [x] 3.2 Run focused Go tests, `task docs:validate`, and the full `task verify` workspace check.
