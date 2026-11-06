# Why SimpleBehav

[Documentation](../README.md#documentation)

Reqnroll (Cucumber/Gherkin for .NET) requires contributors to maintain **four separate files** for a single acceptance test: a `.feature` file and three step-definition partials (`Given`, `When`, `Then`). The cognitive cost compounds quickly:

- Reviewing one scenario means opening four files and mentally stitching them together.
- Adding one scenario requires editing at minimum two files (feature + steps).
- Step text is stringly typed — renames cause silent test drift, not compile errors.
- AI-assisted authoring burns tokens on step-binding boilerplate that carries no semantic information. At ~132 tokens per scenario, the overhead accumulates across every test generation, review, and refactor cycle.

> **Measurable cost:** The MinMaxRateSetting feature has 17 scenarios spread across 578 lines in 4 files (~34 lines, ~132 tokens per scenario). The same coverage in SimpleBehav takes 276 lines in 1 file (~21 lines, ~67 tokens per scenario). See the [comparison](comparison.md).

## What you get

- **Reviewer sanity:** one method = one scenario, one file to open.
- **Compile-time safety:** page interactions are typed C# calls, not regex-matched strings.
- **AI cost:** ~50% fewer tokens to generate or review one test — less context wasted on structural boilerplate.
- **Living docs:** the DSL auto-generates human-readable HTML test reports, giving PMs and QA the readability of Gherkin without the maintenance overhead.
- **Open-source leverage:** publishing the library means the community maintains the DSL layer; teams own only their test logic.

## What changes for a team

New acceptance tests are written entirely in C#, co-located in one file per feature, with auto-generated living documentation published as a static site on every CI run. Existing Reqnroll tests move feature-by-feature as teams touch them. There is no big-bang migration. See [Migration](migration.md).
