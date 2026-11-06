using Microsoft.Playwright;

namespace SimpleBehav;

internal sealed class Assertion(string label, Func<IPage, Task> run) : IAssertion
{
    public string Label { get; } = label;

    public Task RunAsync(ISetupContext context)
    {
        if (context is not SetupContext playwright)
            throw new InvalidOperationException($"See assertions require a Playwright SetupContext, got {context.GetType().Name}");
        return run(playwright.Page);
    }
}

/// <summary>Fluent assertion builders — the review surface for a scenario.</summary>
public static class See
{
    private static IAssertion Make(string label, Func<IPage, Task> run) => new Assertion(label, run);

    public static IAssertion Toast(string partialText, int timeoutMs = 10_000) =>
        Make($"toast containing \"{partialText}\" visible", async page =>
            await page.GetByText(partialText, new() { Exact = false })
                      .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = timeoutMs }));

    public static IAssertion TextVisible(string partialText, int timeoutMs = 10_000) =>
        Make($"\"{partialText}\" visible on page", async page =>
            await page.GetByText(partialText, new() { Exact = false })
                      .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = timeoutMs }));

    public static IAssertion ElementVisible(string selector, int timeoutMs = 10_000) =>
        Make($"element \"{selector}\" visible", async page =>
            await page.Locator(selector)
                      .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = timeoutMs }));

    public static IAssertion ButtonDisabled(string testId, int timeoutMs = 5_000) =>
        Make($"button [{testId}] is disabled", async page =>
        {
            var btn = page.GetByTestId(testId);
            await btn.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = timeoutMs });
            var disabled = await btn.IsDisabledAsync();
            if (!disabled) throw new InvalidOperationException($"Expected button [{testId}] to be disabled but it was enabled");
        });

    public static IAssertion Custom(string label, Func<IPage, Task> assertion) => Make(label, assertion);
}
