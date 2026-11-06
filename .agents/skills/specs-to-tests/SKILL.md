---
name: specs-to-tests
description: Converts numbered lists, prose, shallow Gherkin, and incident notes into declarative SimpleBehav C# test stubs in one step. Refuses input that has no business intent and asks who the actor is, what they need, and what outcome proves it. Use when generating SimpleBehav tests, scenario stubs, or Given/When/Then chains from specs, tickets, or incident notes. A SKILL.md already at .agents/skills/specs-to-tests in the repo wins over the copy shipped in the SimpleBehav package.
compatibility: Requires a .NET test project that references SimpleBehav.
metadata:
  author: Agoda
---

# specs-to-tests

The developer writes whatever they know — imperative flow notes, prose,
shallow Gherkin with implementation labels, incident timelines. The skill
extracts business intent and generates compilable SimpleBehav test stubs.

No intermediate Gherkin file. No separate conversion step. One input, one output.

The `SKILL.md` already in the repo at `.agents/skills/specs-to-tests/` is the copy to follow. The SimpleBehav package installs its own copy on build only when that folder has no `SKILL.md`. It does not replace a skill that is already there, and it does not replace an installed copy after someone edits it.

---

## Input — write what you know

Any of these work:

```
Normal flow:
1. User navigates to YCS page
2. Security token expired → redirected to Authorization.cshtml
3. JS redirects to /{lang}/Login/SignOutWithClearSession
4. LoginController clears session → redirects to /mldc/{lang}/public/login
5. User sees login page ✅
```

```
When a hotel partner's session expires mid-browsing, the system should
redirect them back to the login page so they can authenticate again.
```

```
Given the user is on YCS
When InitializeSessionHandling fires and Server.TransferRequest is called
Then LoginController returns 404
```

All formats are treated the same.

---

## Process

1. **Read the input.**

2. **Extract for EACH flow:**
   - **Actor** — who? (hotel partner, admin, guest)
   - **Precondition** — what state must hold?
   - **Action** — one user-observable verb, ≤ 8 words
   - **Outcome** — one user-visible result

3. **Validate.** If you cannot answer all four questions — the input is purely
   implementation steps with no user intent — **STOP**. Ask the user:
   - "Who is the actor in this flow?"
   - "What does this user need to be able to do?"
   - "What outcome proves it worked or broke?"
   Re-run after the user answers.

4. **Generate** three files directly — no intermediate Gherkin.

---

## Output

For a feature called `<Feature>`:

| File | Purpose |
|---|---|
| `<Feature>Tests.cs` | Test class — chain-only, no implementation |
| `<Feature>Context.cs` | `ISetup` stubs — `throw new NotImplementedException()` |
| `<Feature>Assertions.cs` | `IAssertion` stubs on `partial class See` |

---

## Rules

1. **One `[Test]` = one user scenario = one method = one outcome.**
2. **Method name encodes user need, NOT implementation.**
   `SecurityTokenExpires_PartnerReachesLoginPage` ✅
   `InitializeSessionHandling_TransferRequest_LoginController` ❌
3. **Exactly one `.When<T>()` per chain** — the type chain enforces this at compile time.
4. **`.When` label = one user action, ≤ 8 words.**
5. **`.Then` label = one user-observable outcome.**
6. **No implementation details in any label** — controller names, file names,
   JS code, HTTP status codes, internal method names all belong in the stub body
   the engineer writes later, never in the chain.
7. **Bug-proving variants get `[Category("ProvesBug")]`.**
8. **Stubs throw `NotImplementedException`** — CI stays red until implemented.

---

## Worked example

**Input:**
```
Normal flow:
1. User navigates to YCS page
2. Security token expired → redirected to Authorization.cshtml
3. JS redirects to /{lang}/Login/SignOutWithClearSession
4. LoginController clears session → redirects to /mldc/{lang}/public/login
5. User sees login page ✅

Broken flow (experiment bucket B):
1-3. Same as above
4. [DeprecateLegacyMvcEndpoint] intercepts → HTTP 404 ❌
5. User sees 404 page
```

**`HotelPartnerSessionTests.cs`:**
```csharp
[TestFixture]
public class HotelPartnerSessionTests
{
    [Test]
    public async Task SecurityTokenExpires_PartnerReachesLoginPage()
    {
        await Scenario()
            .Given(HotelPartnerSession.WithExpiredSecurityToken)
            .When<YcsPage>("partner navigates to a protected page")
            .Then("partner reaches the login page", See.OnLoginPage);
    }

    [Test]
    [Category("ProvesBug")]
    public async Task SecurityTokenExpires_LoginControllerDeprecated_PartnerSees404()
    {
        await Scenario()
            .Given(HotelPartnerSession.WithExpiredSecurityToken)
            .Given(Experiment.DeprecateLoginController)
            .When<YcsPage>("partner navigates to a protected page")
            .Then("partner sees 404 instead of login page", See.StatusCodeIs(404));
    }
}
```

**`HotelPartnerSessionContext.cs`:**
```csharp
public static class HotelPartnerSession
{
    public static ISetup WithExpiredSecurityToken => throw new NotImplementedException();
}

public static class Experiment
{
    public static ISetup DeprecateLoginController => throw new NotImplementedException();
}
```

**`HotelPartnerSessionAssertions.cs`:**
```csharp
public static partial class See
{
    public static IAssertion OnLoginPage => throw new NotImplementedException();
    public static IAssertion StatusCodeIs(int code) => throw new NotImplementedException();
}
```

---

## Rejection example

**Input** (no business intent — pure implementation):
```
1. ControllerBase.InitializeSessionHandling fires
2. Server.TransferRequest is called
3. LoginController.Index returns 404
```

**Skill behavior**: STOP. Ask the user:
- "Who is the actor affected by this flow?"
- "What does this user need to be able to do?"
- "What outcome proves it worked or broke?"

---

## Reference

See `references/simplebehav-chain.md` for why multiple `.When()` and mid-flow
`.Then()` won't compile — the type chain is the structural enforcement.

Worked inputs, the rejected case, and the expected C# output are in `examples/`.
