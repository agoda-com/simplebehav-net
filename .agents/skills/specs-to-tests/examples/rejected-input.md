# Example: Rejected input (no business intent)

This input contains only implementation steps. The skill cannot identify
the actor, precondition, action, or outcome from the user's perspective.

The skill MUST stop here and ask the user before generating anything.

---

1. ControllerBase.InitializeSessionHandling fires
2. _sessionProfile.IsLogin is false
3. Server.TransferRequest("/{lang}/Login") is called
4. LoginController.Index returns HTTP 404 due to [DeprecateLegacyMvcEndpointAttribute]

---

Expected skill behavior:

STOP. Ask the user:
- "Who is the actor affected by this flow?"
- "What does this user need to be able to do?"
- "What outcome proves it worked or broke?"

Only after the user provides business framing should the skill generate stubs.
