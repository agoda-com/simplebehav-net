using Microsoft.Playwright;

namespace SimpleBehav.Tests.Support;

/// <summary>Page object for the demo checkout page served by <see cref="Pages.Checkout"/>.</summary>
public sealed class CheckoutPage(IPage page)
{
    public const string Url = "https://simplebehav.test/checkout";

    public Task Open() => page.GotoAsync(Url);

    public async Task Submit()
    {
        await Open();
        await page.GetByTestId("submit").ClickAsync();
    }

    public async Task ClearCard()
    {
        await Open();
        await page.GetByTestId("card").FillAsync("");
    }

    public async Task SelectCurrency(string currency)
    {
        await Open();
        await page.GetByTestId("currency").FillAsync(currency);
    }
}
