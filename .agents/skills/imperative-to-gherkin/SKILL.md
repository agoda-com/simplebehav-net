---
name: imperative-to-gherkin
description: Converts numbered imperative lists, prose, and shallow Gherkin with implementation-leaking labels into declarative Gherkin scenarios. Refuses input that has no business intent and asks who the actor is, what they need, and what outcome proves it. Use when turning flow notes, tickets, or incident timelines into a feature file. For SimpleBehav C# stubs, use specs-to-tests instead. A SKILL.md already at .agents/skills/imperative-to-gherkin in the repo wins over the copy shipped in the SimpleBehav package.
metadata:
  author: Agoda
---

# imperative-to-gherkin

Reads any form of flow description and produces well-formed Gherkin scenarios.
The LLM extracts business intent — dropping implementation details — and enforces
correct Gherkin structure so the output is readable as living specification by anyone,
regardless of technical background.

Works with any test framework. For SimpleBehav specifically, use `specs-to-tests`
which additionally generates compilable C# stubs.

The `SKILL.md` already in the repo at `.agents/skills/imperative-to-gherkin/` is the copy to follow. The SimpleBehav package installs its own copy on build only when that folder has no `SKILL.md`. It does not replace a skill that is already there, and it does not replace an installed copy after someone edits it.

---

## Input formats accepted

**A. Numbered imperative flow:**
```
Normal flow:
1. User navigates to YCS page
2. Security token expired → redirected to Authorization.cshtml
3. JS redirects to /{lang}/Login/SignOutWithClearSession
4. LoginController clears session → redirects to /mldc/{lang}/public/login
5. User sees login page ✅
```

**B. Prose description:**
```
When a hotel partner's session expires mid-browsing, the system should
redirect them back to the login page so they can authenticate again.
```

**C. Shallow Gherkin (structured but labels leak implementation):**
```
Given the user is on YCS
When InitializeSessionHandling fires and Server.TransferRequest is called
Then LoginController returns 404
```

All formats treated the same — the LLM extracts business intent and rewrites
labels declaratively.

---

## Process

1. **Read the input.**

2. **Extract for EACH flow:**
   - **Actor** — who is doing this? (hotel partner, admin, guest user)
   - **Precondition** — what state must hold before the action?
   - **Action** — one user-observable verb, ≤ 8 words
   - **Outcome** — one user-visible result

3. **Validate.** If you cannot answer all four questions from the input —
   the input contains only implementation steps with no user intent — **STOP**.
   Do NOT generate Gherkin. Ask the user:
   - "Who is the actor in this flow?"
   - "What does this user need to be able to do?"
   - "What outcome proves it worked or broke?"
   Then re-run the process with the answers.

4. **Generate** well-formed Gherkin. See rules below.

---

## Output format

```gherkin
Feature: <business capability — what users can do>
  In order to <business value>
  As a <actor>
  I need to <user need>

  Background: (optional — shared preconditions across all scenarios)
    Given <shared state>

  Scenario: <outcome from the user's perspective>
    Given <precondition>
    When <single user action>
    Then <observable outcome>
    And <additional outcome> (optional)

  Scenario: <variant outcome>
    Given <precondition>
    And <additional precondition>
    When <single user action>
    Then <observable outcome>
```

---

## Rules the generator MUST follow

1. **Feature title = business capability**, not a technical description.
   Good: `Hotel partners can always reach the login page`
   Bad:  `LoginController session handling`

2. **One `When` per scenario** — the single user action that triggers the outcome.
   If the input has multiple steps, collapse to the user-visible trigger.

3. **`Given` = precondition/state**, not an action.
   Good: `Given my session has expired`
   Bad:  `Given InitializeSessionHandling fires`

4. **`When` = one user action**, ≤ 8 words, user-observable.
   Good: `When I navigate to a protected page`
   Bad:  `When Server.TransferRequest is called and LoginController intercepts`

5. **`Then` = one user-observable outcome.**
   Good: `Then I am redirected to the login page`
   Bad:  `Then LoginController.Index returns HTTP 302`

6. **Implementation details MUST NOT appear in any label.**
   Method names, file names, class names, HTTP status codes (unless the user
   observes them directly), JS code — none of these belong in Gherkin labels.
   They belong in step definitions, not the feature file.

7. **Bug-proving scenarios get a `@proves-bug` tag.**

8. **Use first-person (`I`) for the actor** when the actor is the end user.
   Use third-person (`the hotel partner`) when writing from an observer perspective
   or when multiple actors interact.

---

## Examples

### Input (imperative — real incident)
```
Normal flow (before experiment):
1. User navigates to YCS page
2. Security token expired → redirected to Authorization.cshtml
3. JS redirects to /{lang}/Login/SignOutWithClearSession
4. LoginController clears session → redirects to /mldc/{lang}/public/login
5. User sees login page ✅

Broken flow (experiment bucket B):
1. User navigates to YCS page
2. Security token expired → redirected to Authorization.cshtml
3. JS redirects to /{lang}/Login/SignOutWithClearSession
4. [DeprecateLegacyMvcEndpoint] intercepts → HTTP 404 ❌
5. User sees 404 page
```

### Output (declarative Gherkin)
```gherkin
Feature: Hotel partners can always reach the login page when their session ends
  In order to continue managing my property
  As a hotel partner using Legacy YCS
  I need to be redirected to the login page whenever my session expires

  Scenario: Session expires while browsing a protected page
    Given my security token has expired
    When I navigate to a protected YCS page
    Then I am redirected to the login page

  @proves-bug
  Scenario: Session expires while LoginController is being deprecated
    Given my security token has expired
    And I am in the LoginController deprecation experiment
    When I navigate to a protected YCS page
    Then I see a 404 error instead of the login page
```

---

### Rejected input example

**Input** (no business intent):
```
1. ControllerBase.InitializeSessionHandling fires
2. _sessionProfile.IsLogin is false
3. Server.TransferRequest("/{lang}/Login") is called
4. LoginController.Index returns HTTP 404
```

**Skill behavior**: STOP. Ask the user:
- "Who is the actor affected by this flow?"
- "What does this user need to be able to do?"
- "What outcome proves it worked or broke?"

---

## After generating

If the user confirms the output looks right, ask:

> "Would you like me to generate SimpleBehav test stubs from these scenarios?
> Use `/specs-to-tests` to convert this Gherkin into compilable C# stubs."

This lets `imperative-to-gherkin` feed into `specs-to-tests` as a two-step pipeline:
fuzzy input → clean Gherkin → compilable test stubs.
