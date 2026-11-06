using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace SimpleBehav;

/// <summary>
/// Builder that resolves a page object from a DI container (<see cref="IServiceProvider"/>)
/// instead of constructing it via <c>Activator.CreateInstance</c> like <see cref="PlaywrightBuilder"/>.
/// </summary>
public sealed class DependencyInjectionBuilder<T> : ISystemUnderTestBuilder
    where T : class
{
    private readonly Func<IServiceProvider> _getServices;
    private readonly Func<T, Task> _configure;
    private readonly Func<T, IPage> _getPage;

    private IServiceScope? _scope;
    private T? _instance;

    /// <summary>
    /// Sentinel setup that triggers DI resolution and the configure delegate.
    /// Pass as the first Given() in every scenario using this builder.
    /// </summary>
    public static readonly ISetup Boot = new BootSetup();

    private sealed class BootSetup : ISetup
    {
        public string Label => string.Empty;
    }

    /// <param name="getServices">Returns the root <see cref="IServiceProvider"/> — called once per scenario.</param>
    /// <param name="configure">Called after resolution: wire auth, stubs, navigation, etc.</param>
    /// <param name="getPage">Extracts the <see cref="IPage"/> from <typeparamref name="T"/> for tracing and assertions.</param>
    public DependencyInjectionBuilder(
        Func<IServiceProvider> getServices,
        Func<T, Task> configure,
        Func<T, IPage> getPage)
    {
        _getServices = getServices;
        _configure = configure;
        _getPage = getPage;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public ISetupContext CreateContext()
    {
        _scope?.Dispose();
        _scope = _getServices().CreateScope();
        _instance = null;
        return new DiContext();
    }

    public async Task ApplyAsync(ISetupContext ctx, ISetup setup)
    {
        if (setup is not BootSetup) return;

        _instance = _scope!.ServiceProvider.GetRequiredService<T>();
        await _configure(_instance);
    }

    public TPage Construct<TPage>(ISetupContext ctx) where TPage : class
    {
        if (typeof(TPage) == typeof(T))
            return (_instance as TPage)!;

        throw new InvalidOperationException(
            $"DependencyInjectionBuilder<{typeof(T).Name}> cannot construct {typeof(TPage).Name}");
    }

    public object ConstructSut(ISetupContext ctx) => _getPage(_instance!);

    public Task DisposeAsync()
    {
        _scope?.Dispose();
        _scope = null;
        return Task.CompletedTask;
    }

    private sealed class DiContext : ISetupContext { }
}
