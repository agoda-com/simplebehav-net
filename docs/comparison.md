# Comparison with Reqnroll

[Documentation](../README.md#documentation)

Counts are for MinMaxRateSetting, 17 scenarios. Global one-time setup (`SimpleBehavSetup.cs`, `.csproj`) is excluded from per-feature counts. That setup is written once and amortized across all features.

| | Reqnroll | SimpleBehav | Change |
|---|---:|---:|---:|
| Files per feature | 4 | 1 | **−75%** |
| Total lines (MinMaxRateSetting) | 578 | 276 | **−52%** |
| Total words ≈ tokens | 2,250 | 872 | **−61%** |
| Lines per test | ~34 | ~21 | **−38%** |
| Tokens per test | ~132 | ~67 | **−49%** |

The same feature is the example in [Why SimpleBehav](why.md).
