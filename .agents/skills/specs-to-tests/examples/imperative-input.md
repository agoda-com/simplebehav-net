# Example: Imperative input (real incident notes)

This is the format engineers naturally produce after an incident.
The skill must extract business intent and generate declarative stubs.

---

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
