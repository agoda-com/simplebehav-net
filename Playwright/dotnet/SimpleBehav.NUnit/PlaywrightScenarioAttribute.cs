using Microsoft.Playwright;
using NUnit.Framework;
using NUnit.Framework.Interfaces;

namespace SimpleBehav.NUnit;

/// <summary>
/// Wires Playwright browser/page lifecycle and Spec configuration to a plain test class.
/// Apply to any class — no base class required.
///
///   [PlaywrightScenario]
///   public class CheckoutTests : IScenarioDefaults
///   {
///       public GivenToken Configure(GivenToken token) =>
///           token.WithAuthenticatedUser().WithPopulatedInventory();
///
///       [Test]
///       public Task PlaceOrder_ShowsConfirmation() => Scenario()
///           .Given(Orders.ValidCard)
///           .When&lt;CheckoutPage&gt;("user places order", ctx => ctx.SubmitOrder())
///           .Then("order confirmation shown", See.TextVisible("Order confirmed."));
///   }
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class PlaywrightScenarioAttribute : Attribute, ITestAction
{
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public ActionTargets Targets => ActionTargets.Suite | ActionTargets.Test;

    // ─── Suite: one browser per fixture ──────────────────────────────────────

    public void BeforeTest(ITest test)
    {
        if (test.IsSuite)
        {
            Spec.ResolveVariant = ResolveVariant;
            _playwright = Playwright.CreateAsync().GetAwaiter().GetResult();
            _browser = _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = !string.Equals(
                    Environment.GetEnvironmentVariable("SIMPLEBEHAV_HEADLESS"),
                    "false", StringComparison.OrdinalIgnoreCase),
            }).GetAwaiter().GetResult();
        }
        else
        {
            var page = _browser!.NewPageAsync().GetAwaiter().GetResult();
            var artifactsDir = Environment.GetEnvironmentVariable("SIMPLEBEHAV_ARTIFACTS") ?? "artifacts";
            var projectName = test.Fixture?.GetType().Assembly.GetName().Name;
            Spec.Configure(new PlaywrightBuilder(page), artifactsDir, projectName);

            Spec.ConfigureScenario = test.Fixture is IScenarioDefaults defaults
                ? defaults.Configure
                : null;
        }
    }

    public void AfterTest(ITest test)
    {
        if (test.IsSuite)
        {
            _browser?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _playwright?.Dispose();
        }
        // page is closed by PlaywrightBuilder.DisposeAsync on scenario completion
    }

    private static string? ResolveVariant()
    {
        var args = TestContext.CurrentContext.Test.Arguments;
        return args.Length > 0 ? string.Join(", ", args) : null;
    }
}

/// <summary>
/// Implement on a test class to pre-fill shared context for every scenario in the fixture.
/// The attribute calls Configure(token) before each test.
/// </summary>
public interface IScenarioDefaults
{
    GivenToken Configure(GivenToken token);
}
