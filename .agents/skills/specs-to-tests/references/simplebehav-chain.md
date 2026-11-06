# SimpleBehav Type Chain Rules

The chain enforces correct Gherkin structure at compile time.
Understanding these rules is required before generating test stubs.

## The token chain

```
Spec.Scenario()   → GivenToken
  .Given(setup)   → GivenToken   (can repeat)
  .When<T>(label) → WhenToken    (exactly once — WhenToken has no .When())
  .Then(label)    → ThenToken    (awaitable)
  .And(label)     → ThenToken    (can repeat)
```

## What this prevents at compile time

| Wrong pattern | Why it won't compile |
|---|---|
| Multiple `.When()` | `WhenToken` has no `.When()` method |
| `.Then()` before `.When()` | `GivenToken` has no `.Then()` method |
| Skipping `.When()` | `GivenToken` has no `.Then()` method |
| Awaiting before `.Then()` | `WhenToken` is not awaitable |

## What this means for generation

1. **One `When` per test** — collapse all imperative steps into the single
   user-observable action. The implementation details go in the `ISetup`
   and `IAssertion` bodies, never in the chain.

2. **`Given` is setup, not action** — it applies preconditions to the context.
   An engineer will implement it as `Setup.Create("label", async () => { ... })`.

3. **`Then` and `And` are assertions** — observable outcomes only.
   An engineer will implement them as `See.Custom("label", async page => { ... })`.

4. **Stubs compile but throw** — `throw new NotImplementedException()` on every
   stub makes CI red until the engineer implements them. This is intentional —
   the spec exists and is provably incomplete, not silently broken.

## Example: correct chain

```csharp
await Scenario()
    .Given(HotelPartnerSession.WithExpiredSecurityToken)  // precondition
    .When<YcsPage>("partner navigates to a protected page") // ONE action
    .Then("partner reaches the login page", See.OnLoginPage); // outcome
```

## Example: what you cannot generate

```csharp
// WON'T COMPILE — WhenToken has no .When()
await Scenario()
    .Given(...)
    .When<YcsPage>("security token expires")
    .When<YcsPage>("JS redirects to SignOutWithClearSession")  // ← error
    .Then(...);
```
