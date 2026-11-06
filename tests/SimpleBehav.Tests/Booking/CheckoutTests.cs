using SimpleBehav.NUnit;
using SimpleBehav.Tests.Support;
using static SimpleBehav.Spec;

namespace SimpleBehav.Tests.Booking;

[PlaywrightScenario]
public class CheckoutTests
{
    [Test]
    public async Task PlaceOrder_WithValidCard_ShowsConfirmation()
    {
        await Scenario()
            .Given(Pages.Checkout)
            .When<CheckoutPage>("user places the order", page => page.Submit())
            .Then("order confirmation is shown", See.TextVisible("Order confirmed"))
            .And("order number is shown", See.TextVisible("AG-40128"));
    }

    [Test]
    public async Task PlaceOrder_WithoutCard_KeepsSubmitDisabled()
    {
        await Scenario()
            .Given(Pages.Checkout)
            .When<CheckoutPage>("user clears the card number", page => page.ClearCard())
            .Then("submit stays disabled", See.ButtonDisabled("submit"));
    }

    [TestCase("USD")]
    [TestCase("THB")]
    public async Task PlaceOrder_ShowsTotal_InSelectedCurrency(string currency)
    {
        await Scenario()
            .Given(Pages.Checkout)
            .When<CheckoutPage>("user selects the currency", page => page.SelectCurrency(currency))
            .Then("total is shown in that currency", See.TextVisible($"Total: {currency}"));
    }
}
