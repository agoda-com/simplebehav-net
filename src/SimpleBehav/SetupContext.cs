using Microsoft.Playwright;

namespace SimpleBehav;

/// <summary>
/// Playwright-specific setup context. Used by PlaywrightAdapter to accumulate
/// route handlers and init scripts before applying them to IPage.
/// </summary>
public sealed class SetupContext : ISetupContext
{
    private readonly IPage _page;
    private readonly List<(string Pattern, Func<IRoute, Task> Handler)> _routeHandlers = [];
    private readonly List<string> _initScripts = [];

    public SetupContext(IPage page) => _page = page;

    public IPage Page => _page;

    public void MockRoute(string pattern, Func<IRoute, Task> handler) =>
        _routeHandlers.Add((pattern, handler));

    public void AddInitScript(string script) =>
        _initScripts.Add(script);

    public async Task ApplyAsync()
    {
        foreach (var script in _initScripts)
            await _page.AddInitScriptAsync(script);
        foreach (var (pattern, handler) in _routeHandlers)
            await _page.RouteAsync(pattern, route => handler(route));
    }
}
