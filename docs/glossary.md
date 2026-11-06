# Glossary

[Documentation](../README.md#documentation)

| Term | Definition |
|---|---|
| AAA | Arrange / Act / Assert — test structure pattern. Mapped to Given / When / Then in SimpleBehav. |
| BFF | Backend For Frontend — the ASP.NET Core server under test. |
| DSL | Domain-Specific Language — the fluent C# API provided by SimpleBehav. |
| Living Docs | Auto-generated HTML test reports that describe system behaviour in human-readable form, kept in sync with the test suite. |
| Pact | Contract testing tool. The consumer defines expected interactions; the provider verifies them. Used here to generate WireMock stubs for downstream services. |
| Reqnroll | .NET implementation of Cucumber / Gherkin. The framework these docs compare against. |
| SimpleBehav | This library. |
| WAF | `WebApplicationFactory<Program>` — ASP.NET Core test host that runs the real BFF in-process. |
