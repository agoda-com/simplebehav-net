using Microsoft.Playwright;

namespace SimpleBehav;

/// <summary>
/// Built-in builder for browser-only (FE/BFF mock) scenarios.
/// Setups that implement IPlaywrightSetup can register routes and init scripts.
/// When&lt;T&gt; constructs T via its public IPage constructor.
/// Starts a Playwright trace on every scenario; the zip is written alongside demo.html.
/// </summary>
public sealed class PlaywrightBuilder : ISystemUnderTestBuilder, IScenarioTrace
{
    private static readonly object TraceViewerCopyLock = new();
    private readonly IPage _page;

    public PlaywrightBuilder(IPage page) => _page = page;

    public Task InitializeAsync() => Task.CompletedTask;

    public ISetupContext CreateContext() => new SetupContext(_page);

    public async Task ApplyAsync(ISetupContext ctx, ISetup setup)
    {
        if (ctx is not SetupContext playwrightCtx)
            throw new InvalidOperationException($"PlaywrightBuilder requires SetupContext, got {ctx.GetType().Name}");

        if (setup is IPlaywrightSetup playwrightSetup)
            playwrightSetup.Apply(playwrightCtx);
        else
            throw new InvalidOperationException($"PlaywrightBuilder cannot apply setup of type {setup.GetType().Name}. Implement IPlaywrightSetup.");

        await playwrightCtx.ApplyAsync();
    }

    public T Construct<T>(ISetupContext ctx) where T : class
    {
        if (typeof(T) == typeof(IPage))
            return (_page as T)!;

        return (T)Activator.CreateInstance(typeof(T), _page)!;
    }

    public async Task StartTraceAsync() =>
        await _page.Context.Tracing.StartAsync(new()
        {
            Screenshots = true,
            Snapshots = true,
            Sources = false,
        });

    public async Task StopTraceAsync(string tracePath) =>
        await _page.Context.Tracing.StopAsync(new() { Path = tracePath });

    void IScenarioTrace.EnsureViewer(string artifactsRoot) => EnsureTraceViewer(artifactsRoot);

    public static void EnsureTraceViewer(string artifactsRoot)
    {
        var source = Path.Combine(
            AppContext.BaseDirectory,
            ".playwright", "package", "lib", "vite", "traceViewer");
        if (!Directory.Exists(source))
            throw new DirectoryNotFoundException(
                $"Playwright Trace Viewer assets were not found at \"{source}\".");

        var destination = Path.Combine(artifactsRoot, "trace-viewer");
        lock (TraceViewerCopyLock)
        {
            if (File.Exists(Path.Combine(destination, "index.html")))
                return;

            foreach (var sourceFile in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(source, sourceFile);
                var destinationFile = Path.Combine(destination, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
                File.Copy(sourceFile, destinationFile, overwrite: true);
            }
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

/// <summary>
/// Marker interface for setups compatible with PlaywrightBuilder.
/// Implement this to register routes and init scripts on the browser context.
/// </summary>
public interface IPlaywrightSetup : ISetup
{
    void Apply(SetupContext ctx);
}
