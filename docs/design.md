# Design

[Documentation](../README.md#documentation)

SimpleBehav is a thin DSL over a test runner and a system-under-test adapter. On .NET that runner is NUnit and the adapter is Playwright: a scenario compiles to a standard NUnit `[Test]` method. Kotlin scenarios run under JUnit 5, and TypeScript scenarios run under the Node test runner. All three write the same `bundle.json`, `cucumber.json`, and suite `index.html`.

## What the library includes

- The `SimpleBehav` core DSL (AAA token chain, `See` assertions, `ISystemUnderTestBuilder`)
- `SimpleBehav.NUnit` — NUnit integration (`[PlaywrightScenario]`, `IScenarioDefaults`)
- `DependencyInjectionBuilder<T>` — DI-resolved page objects compatible with `WebApplicationFactory`
- Migration pattern: per-test pact stub lifecycle + shared global infra in `[SetUpFixture]`
- Open-sourcing the library on GitHub / public NuGet
- Static-site living-docs pipeline

## What it does not include

- Migrating existing Reqnroll tests (incremental, and outside this design)
- xUnit or MSTest integration (can be added later; NUnit is the current standard)
- Replacing unit or integration tests — acceptance-layer only

## Assumptions

- Teams continue using Playwright as the browser automation layer.
- Pact contracts remain the mechanism for downstream service stubbing.
- A static host (for example GitHub Pages) is available for the repo.

## Architecture

```text
┌─────────────────────────────────────────────────┐
│  Test Class ([PlaywrightScenario])               │
│  ┌─────────────────────────────────────────────┐ │
│  │  Scenario()                                  │ │
│  │    .When<PageObject>("label", ctx => ...)   │ │
│  │    .Then("label", See.TextVisible("..."))   │ │
│  └─────────────────────────────────────────────┘ │
│                     │                            │
│          ISystemUnderTestBuilder                 │
│    ┌─────────────────┴──────────────────┐        │
│    │ DependencyInjectionBuilder<T>      │        │
│    │  - IServiceProvider scope          │        │
│    │  - configure delegate (auth/stubs) │        │
│    │  - getPage extractor               │        │
│    └────────────────────────────────────┘        │
└─────────────────────────────────────────────────┘
         │                       │
  WebApplicationFactory     Playwright Browser
  (real BFF, AcceptanceTests env)
         │
   Pact Stub Servers (per-test [SetUp]/[TearDown])
```

## Writing a scenario

```csharp
[PlaywrightScenario]
public class MinMaxRateSettingTests : IScenarioDefaults
{
    private PactStubServer _smapiStub = null!;

    [SetUp]
    public async Task SetUp()
    {
        // generate pact contract + start stub server — per test
        var rate = RateConsumerTest.CreateWith(...);
        await rate.GetMinMaxRateSettings_ShouldReturnSettings(DefaultData.Rows);
        _smapiStub = await _stubs.AddStubServer("smapi", ...);
    }

    [TearDown]
    public void TearDown() => _stubs.Dispose();

    public GivenToken Configure(GivenToken token) =>
        FromTestMethod(
            new DependencyInjectionBuilder<MinMaxRateSettingPage>(
                () => SimpleBehavSetup.Services,
                async page => {
                    page.BrowserContextBuilder.WithMockCapiAuth(TestUser.YCSDefaultUser);
                    page.ConfigureWebHosting(s => s.AddMockSmapiServer(_smapiStub.ServerUri));
                    await page.Setup();
                    page.ConfigurePage();
                },
                page => page.CurrentPage),
            TestContext.CurrentContext.Test.MethodName!)
        .And(DependencyInjectionBuilder<MinMaxRateSettingPage>.Boot);

    [Test]
    public Task LandOnPage_TableVisible() =>
        Scenario()
        .When<MinMaxRateSettingPage>("user opens page", ctx => ctx.OpenAsync())
        .Then("settings table is visible",
            See.ElementVisible("[data-testid='min-max-rate-table']"));
}
```

## Other approaches

| Alternative | Pros | Cons | Why we did not take it |
|---|---|---|---|
| Keep Reqnroll, improve step tooling | No migration cost | Fundamental 4-file problem remains; stringly typed; AI token waste unchanged | Treats symptoms, not the cause |
| Raw NUnit + Playwright (no DSL) | No new dependency | No living docs; no structured reporting; each team reinvents conventions | Loss of documentation output; fragmentation |
| Playwright Test (TypeScript) | First-class Playwright support | Requires JS infrastructure alongside .NET; C# BFF tests need C# tooling | Wrong language layer for BFF acceptance tests |

## How the library is tested

**Unit tests.** The core DSL token chain (`GivenToken → WhenToken → ThenToken`) is tested in `SimpleBehav.Tests`. Those tests check setup application order, builder lifecycle, and assertion dispatch.

**Acceptance tests.** The library is validated by dogfooding: `SimpleBehav.Tests` uses SimpleBehav to test itself against a real Playwright browser.

**Contract tests.** The library itself has no Pact contracts. Consumer-side Pact usage (downstream stub lifecycle) is validated via the MinMaxRateSetting proof of concept and documented in the migration guide.

**Feature flags.** None. The library has no feature-flag dependency. Adoption is opt-in per team.
