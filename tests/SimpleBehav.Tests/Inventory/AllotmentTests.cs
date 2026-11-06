using Microsoft.Playwright;
using SimpleBehav.NUnit;
using SimpleBehav.Tests.Support;
using static SimpleBehav.Spec;

namespace SimpleBehav.Tests.Inventory;

[PlaywrightScenario]
public class AllotmentTests
{
    [Test]
    public async Task SaveAllotment_WithinContract_ShowsSaveCompleted()
    {
        await Scenario()
            .Given(Pages.Allotment)
            .When<IPage>("user saves the allotment", async page =>
            {
                await page.GotoAsync("https://simplebehav.test/allotment");
                await page.GetByTestId("save").ClickAsync();
            })
            .Then("save confirmation is shown", See.Toast("Save completed."));
    }

    [Test]
    public async Task SaveAllotment_AboveContract_ShowsRejection()
    {
        await Scenario()
            .Given(Pages.Allotment)
            .When<IPage>("user saves more rooms than contracted", async page =>
            {
                await page.GotoAsync("https://simplebehav.test/allotment");
                await page.GetByTestId("rooms").FillAsync("24");
                await page.GetByTestId("save").ClickAsync();
            })
            .Then("rejection message is shown", See.Toast("exceeds contracted rooms"));
    }
}
