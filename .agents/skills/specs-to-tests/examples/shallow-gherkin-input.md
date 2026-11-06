# Example: Shallow Gherkin input (implementation leaking into labels)

This is what engineers produce when they know Gherkin syntax but think imperatively.
Labels describe code mechanics, not user needs. The skill must rewrite declaratively.

---

Scenario: Normal flow
  Given the user has a YCS session
  When InitializeSessionHandling fires and the YCS session is valid
  Then LoginController.Index is called via Server.TransferRequest
  And LoginController redirects to /mldc/{lang}/public/login
  And the user sees the login page

Scenario: Broken flow - experiment bucket B
  Given the user has a YCS session
  And the user is in experiment bucket B
  When InitializeSessionHandling fires and Server.TransferRequest("/{lang}/Login") is called
  Then [DeprecateLegacyMvcEndpointAttribute] intercepts the request
  And LoginController returns HTTP 404
  And the user sees a 404 page
