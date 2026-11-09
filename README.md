# SimpleBehav

**Specs that prove themselves.** A behavior-driven test DSL for C#, Kotlin, and TypeScript.
Structured like Gherkin, built like production code.

A requirement is normally written three times — in the ticket, in a feature file, in test code — and
then drifts. SimpleBehav collapses that into one artifact: a C# scenario that reads like the
requirement *and is* the test. CI then publishes it as documentation, with the evidence attached.

## The vision

```
product writes a prompt ─▶ agent writes the scenario ─▶ CI runs it ─▶ pages show the spec,
                                                                      the proof, and what changed
```

| Stage | Status |
|---|---|
| Scenarios as plain, compiler-checked C# (Given / When / Then) | ✅ shipped |
| Per-scenario result + Playwright trace, one report page per test file | ✅ shipped |
| **Living documentation** generated from test source, published as a static site | ✅ shipped |
| Pass/fail on the living doc, `suite.json` manifest, and a "changes since baseline" list (added / removed / reworded / now failing) | ✅ shipped |
| Visual "what changed" — step-level and screenshot diff, MR comment | 🔜 next |
| Prompt → scenario via an MCP engine for Claude, extending missing building blocks on request | 🔜 planned |
| Kotlin and TypeScript implementations emitting the same artifact formats (`bundle.json`, `cucumber.json`, suite `index.html`) | ✅ shipped |
| Living documentation generated from Kotlin and TypeScript source | ✅ shipped |

The Given / When / Then chain is the same in each language. Each language keeps its own build root: `SimpleBehav.sln` for C#, `kotlin/` for Kotlin (the DSL, the Playwright adapter, and a Ktor host adapter), and `package.json` projects under `DSL/typescript` and `Playwright/typescript`. `simplebehav-gen --livingdoc` reads `.cs`, `.kt`, and `.ts` test files.

Why this suits agents: no step-binding layer to get wrong, about half the tokens per scenario of
Gherkin ([comparison](docs/comparison.md)), and the C# compiler validates the structure
of anything an agent writes.

---

## Why not Cucumber / SpecFlow?

Gherkin-based frameworks are a natural first choice for behavior-driven testing, but in practice they introduce friction that often outweighs the benefit:

**1. Diff noise in code review.** Feature files and step definitions change together but live in separate files. A single behavior change produces a scattered diff that reviewers must mentally stitch back together — increasing review time and the chance of missing issues.

**2. Token overhead for AI agents.** Agents generating or editing tests must understand and maintain the mapping between `.feature` files and step implementations — two layers, double the context. Natural-language files that look simple are expensive to reason about correctly.

**3. An abstraction layer that buys little.** Step registries, regex matching, and binding frameworks exist to let non-engineers write tests. If your tests are authored and maintained by engineers, this machinery adds build complexity without meaningful return.

**4. Imperative steps leak into declarative scenarios.** The strictest Gherkin reads as pure business intent. In practice, `Given I wait for the spinner to disappear` ends up in `.feature` files — coupling specification language to implementation detail and defeating the readability goal.

---

## What SimpleBehav does instead

Write scenarios directly in C# using a fluent Given / When / Then (or Arrange / Act / Assert) chain. Tests live in normal code files, diff cleanly, and compose through standard language features — no binding layer, no regex, no parallel file to maintain.

```csharp
[Test]
public async Task AddItem_ToCart_ShowsCartUpdatedNotification()
{
    await Scenario()
        .Given(Products.Available)
        .When<CartPage>("user adds item to cart", async ctx =>
        {
            await ctx.Open(BaseUrl);
            await ctx.AddItem("Wireless Headphones", qty: 1);
        })
        .Then("cart updated notification is shown", See.TextVisible("Item added to cart."));
}
```

The scenario name is captured from the method name automatically via `[CallerMemberName]`. Shared context (`Products.Available`) composes without a step registry. The `When` block is the only place where UI interaction lives, and assertions stay declarative.

### Shared context without repetition

Implement `IScenarioDefaults` on the test class to pre-fill context for every scenario in it:

```csharp
[PlaywrightScenario]
public class CheckoutTests : IScenarioDefaults
{
    public GivenToken Configure(GivenToken token) =>
        token.WithAuthenticatedUser().WithPopulatedInventory();

    [Test]
    public async Task PlaceOrder_WithValidCard_ShowsConfirmation() =>
        await Scenario()
            .Given(Orders.ValidCard)
            .When<CheckoutPage>("user places order", page => page.SubmitOrder())
            .Then("order confirmation is shown", See.TextVisible("Order confirmed."));
}
```

`WithAuthenticatedUser()` and `WithPopulatedInventory()` apply to every test in the class.
Scenario-specific setups (`Orders.ValidCard`) are added inline.

### Artifact output

Reports are published one page per test file, in a tree that mirrors the test source layout:

```
artifacts/
  trace-viewer/                        static Playwright Trace Viewer
  SimpleBehav.Tests/
    Booking/CheckoutTests/
      index.html                       suite report — every scenario in CheckoutTests.cs
      placeorderwithvalidcardshowsconfirmation/
        bundle.json                    SimpleBehav's complete scenario result
        cucumber.json                  for Cucumber-compatible consumers
        trace.zip                      Playwright execution trace
```

The suite report lists each scenario's Given/When/Then steps next to the Playwright trace of the
selected scenario; selecting a scenario, or a step within it, loads that trace in the viewer.

Trace Viewer assets ship inside the artifact directory, so reports stay within the published
site rather than sending trace data to an external service.

### Living documentation

`simplebehav-gen` turns the test source into one screen. Point it at a directory of C#, Kotlin, or
TypeScript tests. Click a scenario and the Given / When / Then spec is on the left and its Playwright
trace is on the right. The spec needs no test run, so it is always current; add the run's artifacts
and every scenario also shows its result:

```
simplebehav-gen --livingdoc tests/MyApp.Tests --out livingdoc \
  --reports ../MyApp.Tests \     # link each feature to its run report and traces
  --results artifacts \          # join pass/fail and duration from bundle.json
  --baseline baseline.json       # suite.json from an earlier run → "changes since baseline"
```

```
livingdoc/
  index.html                  click a scenario: its spec and its Playwright trace, side by side
  graph.html                  separate LR page graph: Page → Feature → Given → When → Then (reuse of shared Givens)
  suite.json                  machine-readable manifest: stable scenario ids, steps, results (for tooling and agents)
  diff.json                   present when a baseline was supplied
  Booking/CheckoutTests/
    index.html                the same screen, limited to one test file
```

A scenario's id is `<folders>/<TestFile>/<Method>`, so it survives renames of the display text.
`[TestCase]` variants roll up into their scenario (it fails if any variant fails). A missing or
incompatible baseline is skipped rather than failing the build.

GitLab users can include the [`ci/livingdoc.yml`](ci/livingdoc.yml) template, which installs `SimpleBehav.SpecGen`, builds the HTML artifact and publishes to Confluence or a per-merge-request Pages review app. In any other CI, run `simplebehav-gen` as shown above and publish the output directory with your static host.

### Publishing to Confluence

```
simplebehav-gen --livingdoc tests/MyApp.Tests --out livingdoc --results artifacts \
  --confluence --confluence-space SimpleBehav --confluence-scope main \
  --reports-url https://pages.example/latest/MyApp.Tests
```

- **Pages:** an index (features, scenarios, results, changes since baseline), one page per feature (Given/When/Then steps, cases, results, failure messages) and a page graph (Page → Feature → Given → When → Then as tables, since Mermaid and the site's JavaScript don't run in Confluence). They are Confluence storage format: native tables, status lozenges, page links with scenario anchors, and a top navigation line plus previous/next on feature pages. Run reports and traces are external links under `--reports-url` (`<url>/<Folder>/<TestFile>/`); omit it and those links are left out.
- **Space:** `--confluence-space` / `CONFLUENCE_SPACE`, default `SimpleBehav`. The value is tried as a space key, then as an exact space name (resolved to its key). Not found, ambiguous or a personal space aborts before anything is written — it never falls back to another space. Set the real key explicitly once known.
- **Repeatable and isolated:** pages are found by title and updated in place. Each page carries `simplebehav-livingdoc` and `sb-livingdoc-<name>-<scope>` labels; only pages with that label are overwritten, and a same-titled page without it is a conflict that aborts the whole publish. Scope `main` (default branch) and `mr-<iid>` (previews, titled `… (preview mr-<iid>)`) never share titles or labels. Pages that are no longer generated are reported, and trashed only with `--confluence-prune`. `--confluence-name` keeps several projects apart in one space.
- **Credentials** are read from the environment only: `CONFLUENCE_BASE_URL` (`https://<site>.atlassian.net/wiki`), `CONFLUENCE_USER`, `CONFLUENCE_TOKEN` (Atlassian API token). Store them as masked, hidden CI/CD variables; they are never accepted as arguments and error output is scrubbed of the token.
- `--confluence-dry-run` renders the pages to `<out>/confluence/*.xml` without credentials or network.
- Exit codes: `2` configuration error (missing credentials, invalid scope/URL), `3` Confluence error. In the shared template the job is `allow_failure`.

The published layout places the site beside the run reports:

```
public/<latest | mr/<iid> | commit/<sha>>/
  livingdoc/                  the specification, its results and what changed
  <Assembly>/…/index.html     the evidence (traces)
```

Other `simplebehav-gen` modes: `simplebehav-gen <Tests.cs> --out <dir>` (single-file markdown) and
`simplebehav-gen --from-spec spec.md --out <dir>` (structured markdown → test stubs).

---

## Documentation

| Page | What it covers |
|---|---|
| [Why SimpleBehav](docs/why.md) | The cost of a separate feature file and step bindings |
| [Design](docs/design.md) | What the library includes, how a scenario is built, how the library is tested |
| [Comparison](docs/comparison.md) | File, line, and token counts against Reqnroll |
| [Glossary](docs/glossary.md) | Terms used in these pages |
| [Agent skills](docs/skills/README.md) | Skills that ship with the library |

## Packages

Install from NuGet once the first release is published:

```
dotnet add package SimpleBehav.NUnit
dotnet tool install --global SimpleBehav.SpecGen
```

| Package | Purpose |
|---|---|
| `SimpleBehav` | Core DSL — framework-agnostic |
| `SimpleBehav.NUnit` | NUnit integration — `[PlaywrightScenario]`, `IScenarioDefaults`, auto-variant naming |
| `SimpleBehav.SpecGen` | `simplebehav-gen` dotnet tool — living documentation and stub generation |

`SimpleBehav` and `SimpleBehav.NUnit` do not publish Playwright, NUnit, or the dependency-injection abstractions as NuGet dependencies. Reference them from the test project so a pin in that project cannot conflict with the library:

| Peer package | Minimum |
|---|---|
| `Microsoft.Playwright` | 1.63.0 |
| `Microsoft.Extensions.DependencyInjection.Abstractions` | 8.0.0, when a scenario uses `DependencyInjectionBuilder` |
| `NUnit` | 3.13.3, for `SimpleBehav.NUnit` |

The `SimpleBehav` package also ships [Agent Skills](https://agentskills.io/specification) for turning flow notes into scenarios. The same files are committed at [`.agents/skills/`](.agents/skills), the universal directory Cursor, Codex, OpenCode, and other spec-compliant agents read.

NuGet restore cannot copy files into that directory. The package imports a build target that does it on the next build, including when `SimpleBehav` is only a transitive dependency. The skills land in the git repo root at `.agents/skills/`.

A skill folder that already has a `SKILL.md` is left as it is, so the agent keeps reading the repo's copy. A copy this package installed is refreshed on later builds until someone edits it; the edit is then left in place. The installed file ends with a `simplebehav-skill-source: nuget:SimpleBehav` marker so the agent can see it came from the package. Each installed folder gets a `.gitignore` so the generated instructions stay out of source control. Commit those `.gitignore` files. Set `EnableEmbeddedAgentSkills` to `false` in the project or `Directory.Build.props` to skip the copy.
