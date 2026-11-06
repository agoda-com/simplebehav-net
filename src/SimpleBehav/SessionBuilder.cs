using System.Runtime.CompilerServices;
using Microsoft.Playwright;
using SimpleBehav.Reporter;

namespace SimpleBehav;

// ─── Framework contracts ──────────────────────────────────────────────────────

/// <summary>
/// An opaque precondition. The framework passes it to ISystemAdapter.ApplyAsync —
/// the adapter decides what it means for the system under test.
/// </summary>
public interface ISetup
{
    string Label { get; }
}

/// <summary>
/// Adapter-owned context built per scenario. The framework passes it through
/// but never inspects it — the adapter fills it during Apply and reads it in Construct.
/// </summary>
public interface ISetupContext { }

/// <summary>
/// Pluggable builder for the system under test. One implementation per system type.
/// The framework drives the lifecycle — the builder owns all system knowledge.
/// </summary>
public interface ISystemUnderTestBuilder
{
    /// <summary>Called once per test class — start servers, browsers, etc.</summary>
    Task InitializeAsync();

    /// <summary>Creates a fresh context for one scenario.</summary>
    ISetupContext CreateContext();

    /// <summary>Applies a single setup to the context. Called once per Given.</summary>
    Task ApplyAsync(ISetupContext ctx, ISetup setup);

    /// <summary>
    /// Constructs the typed SUT object from the context.
    /// Called by When&lt;T&gt; just before the action runs.
    /// </summary>
    T Construct<T>(ISetupContext ctx) where T : class;

    /// <summary>Called once per test class — teardown.</summary>
    Task DisposeAsync();
}

// ─── Pre-built setup ──────────────────────────────────────────────────────────

public static class Setup
{
    public static ISetup Create(string label, Action action) =>
        new ActionSetup(label, _ => { action(); return Task.CompletedTask; });

    public static ISetup Create(string label, Func<Task> action) =>
        new ActionSetup(label, _ => action());

    private sealed class ActionSetup(string label, Func<ISetupContext, Task> apply) : ISetup
    {
        public string Label { get; } = label;
        internal Func<ISetupContext, Task> Apply { get; } = apply;
    }
}

// ─── Internal runner ──────────────────────────────────────────────────────────

internal sealed class ScenarioRunner
{
    private readonly string _outDir;
    private readonly string[] _suitePath;
    private readonly string _scenarioName;
    private readonly ISystemUnderTestBuilder _adapter;
    private readonly List<ISetup> _setups = [];
    private string _whenLabel = string.Empty;
    private Func<ISetupContext, Task>? _whenFn;

    internal ScenarioRunner(string outDir, string[] suitePath, string scenarioName, ISystemUnderTestBuilder adapter)
    {
        _outDir = outDir;
        _suitePath = suitePath;
        _scenarioName = scenarioName;
        _adapter = adapter;
    }

    internal void AddSetup(ISetup setup) => _setups.Add(setup);
    internal ISystemUnderTestBuilder GetAdapter() => _adapter;

    internal void SetWhen(string label, Func<ISetupContext, Task> fn)
    {
        _whenLabel = label;
        _whenFn = fn;
    }

    internal async Task ExecuteAsync(IReadOnlyList<(string Label, IAssertion Assertion)> assertions)
    {
        var emitter = new ArtifactEmitter(_scenarioName);
        var status = "pass";
        string? failureMessage = null;
        var ctx = _adapter.CreateContext();

        // Start trace before any interaction
        if (_adapter is PlaywrightBuilder pw)
            await pw.StartTraceAsync();

        try
        {
            await RunGivenAsync(ctx, emitter);
            await RunWhenAsync(ctx, emitter);
            await RunThenAsync(ctx, assertions, emitter);
        }
        catch (Exception ex)
        {
            status = "fail";
            failureMessage = ex.Message;
            emitter.AddStep(new ArtifactStep
            {
                Type = StepType.Assert,
                Label = $"FAILED: {failureMessage}",
                DurationMs = 0,
                Error = failureMessage,
            });
        }

        var bundle = emitter.Build(status);
        var dir = await emitter.EmitAsync(bundle, _outDir, _suitePath);

        // Stop trace and write the zip next to the scenario's bundle
        var tracePath = Path.Combine(dir, "trace.zip");
        if (_adapter is PlaywrightBuilder pwStop)
        {
            await pwStop.StopTraceAsync(tracePath);
            PlaywrightBuilder.EnsureTraceViewer(_outDir);
        }

        // The viewer lives at the artifacts root, the suite page one level per path segment below it
        var traceViewerBase = string.Concat(Enumerable.Repeat("../", _suitePath.Length)) + "trace-viewer/index.html";
        await SuiteReporter.WriteAsync(Path.GetDirectoryName(dir)!, _suitePath[^1], traceViewerBase);

        if (status == "fail")
            throw new InvalidOperationException($"[simplebehav] \"{_scenarioName}\" failed: {failureMessage}");
    }

    private async Task RunGivenAsync(ISetupContext ctx, ArtifactEmitter emitter)
    {
        if (_setups.Count == 0) return;
        var t = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        foreach (var setup in _setups)
            await _adapter.ApplyAsync(ctx, setup);
        var label = string.Join(", ", _setups.Select(s => s.Label).Where(l => l.Length > 0));
        emitter.AddStep(new ArtifactStep
        {
            Type = StepType.Setup,
            Label = label.Length > 0 ? label : "preconditions applied",
            DurationMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - t,
        });
    }

    private async Task RunWhenAsync(ISetupContext ctx, ArtifactEmitter emitter)
    {
        if (_whenFn is null) return;
        var t = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _whenFn(ctx);
        emitter.AddStep(new ArtifactStep
        {
            Type = StepType.Act,
            Label = _whenLabel,
            DurationMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - t,
        });
    }

    private async Task RunThenAsync(ISetupContext ctx, IReadOnlyList<(string Label, IAssertion Assertion)> assertions, ArtifactEmitter emitter)
    {
        var page = TryGetPage(ctx);
        foreach (var (label, assertion) in assertions)
        {
            var t = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            await assertion.RunAsync(page!);
            emitter.AddStep(new ArtifactStep
            {
                Type = StepType.Assert,
                Label = label,
                DurationMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - t,
            });
        }
    }

    private IPage? TryGetPage(ISetupContext ctx)
    {
        try { return _adapter.Construct<IPage>(ctx); } catch { return null; }
    }
}

// ─── Token chain ──────────────────────────────────────────────────────────────

/// <summary>Awaitable — collects assertions, executes on await.</summary>
public sealed class ThenToken
{
    private readonly ScenarioRunner _runner;
    private readonly List<(string Label, IAssertion Assertion)> _assertions = [];

    internal ThenToken(ScenarioRunner runner, string label, IAssertion assertion)
    {
        _runner = runner;
        _assertions.Add((label, assertion));
    }

    public ThenToken And(string label, IAssertion assertion)
    {
        _assertions.Add((label, assertion));
        return this;
    }

    /// <summary>AAA alias.</summary>
    public ThenToken Assert(string label, IAssertion assertion) => And(label, assertion);

    public TaskAwaiter GetAwaiter() => _runner.ExecuteAsync(_assertions.AsReadOnly()).GetAwaiter();

    public static implicit operator Task(ThenToken token) =>
        token._runner.ExecuteAsync(token._assertions.AsReadOnly());
}

/// <summary>Returned by When() — no And(), single execution context enforced.</summary>
public sealed class WhenToken
{
    private readonly ScenarioRunner _runner;

    internal WhenToken(ScenarioRunner runner) => _runner = runner;

    public ThenToken Then(string label, IAssertion assertion) =>
        new(_runner, label, assertion);

    /// <summary>AAA alias.</summary>
    public ThenToken Assert(string label, IAssertion assertion) => Then(label, assertion);
}

/// <summary>Returned by Given()/And() — chains more setups or proceeds to When().</summary>
public sealed class GivenToken
{
    private readonly ScenarioRunner _runner;

    internal GivenToken(ScenarioRunner runner) => _runner = runner;

    public GivenToken Given(ISetup setup) => And(setup);
    public GivenToken And(ISetup setup)
    {
        _runner.AddSetup(setup);
        return this;
    }

    /// <summary>AAA alias.</summary>
    public GivenToken Arrange(ISetup setup) => And(setup);

    /// <summary>
    /// Single execution context — adapter constructs T via ISystemAdapter.Construct&lt;T&gt;(ctx).
    /// No And() on the returned token.
    /// </summary>
    public WhenToken When<T>(string label, Action<T> action) where T : class
    {
        _runner.SetWhen(label, ctx =>
        {
            action(_runner.GetAdapter().Construct<T>(ctx));
            return Task.CompletedTask;
        });
        return new WhenToken(_runner);
    }

    public WhenToken When<T>(string label, Func<T, Task> action) where T : class
    {
        _runner.SetWhen(label, ctx => action(_runner.GetAdapter().Construct<T>(ctx)));
        return new WhenToken(_runner);
    }

    /// <summary>AAA aliases.</summary>
    public WhenToken Act<T>(string label, Action<T> action) where T : class => When<T>(label, action);
    public WhenToken Act<T>(string label, Func<T, Task> action) where T : class => When<T>(label, action);
}

// ─── Entry point ──────────────────────────────────────────────────────────────

/// <summary>
/// Static entry point. Configure once at class level; optionally override per scenario.
///
///   // Class level
///   Spec.Configure(new PlaywrightAdapter(page, outDir));
///
///   // Per-test override
///   Spec.FromTestMethod(builderOverride)
///       .Given(DefaultRates)
///       .When&lt;MinMaxRatePage&gt;("user adds rate", ctx => ctx.AddRate(...))
///       .Then("save confirmed", See.TextVisible("Save completed."));
/// </summary>
public static class Spec
{
    private static ISystemUnderTestBuilder? _defaultAdapter;
    private static string? _outDir;
    private static string _projectName = "tests";

    /// <summary>
    /// Optional hook — set by framework integrations (e.g. SimpleBehav.NUnit) to supply
    /// the current test's parameter variant at runtime. Returns null when not parameterized.
    /// </summary>
    public static Func<string?>? ResolveVariant { get; set; }

    /// <summary>
    /// Optional hook — set by framework integrations to apply shared scenario context
    /// (e.g. default setups) to every Scenario() call in the current fixture.
    /// Set by [PlaywrightScenario] when the fixture implements IScenarioDefaults.
    /// </summary>
    public static Func<GivenToken, GivenToken>? ConfigureScenario { get; set; }

    /// <summary>Base URL for the system under test. Reads SIMPLEBEHAV_BASE_URL env var.</summary>
    public static string BaseUrl =>
        Environment.GetEnvironmentVariable("SIMPLEBEHAV_BASE_URL") ?? "http://localhost:5173";

    /// <param name="projectName">
    /// Test project name — the root of the published report tree. Framework integrations pass
    /// the test assembly name so reports mirror &lt;project&gt;/&lt;folders&gt;/&lt;TestFile&gt;.
    /// </param>
    public static void Configure(ISystemUnderTestBuilder builder, string outDir, string? projectName = null)
    {
        _defaultAdapter = builder;
        _outDir = outDir;
        if (!string.IsNullOrWhiteSpace(projectName)) _projectName = projectName;
    }

    /// <summary>
    /// Entry point for a scenario. CallerMemberName captures the test method name and
    /// CallerFilePath the test file, which becomes the report's suite page.
    /// Import with `using static SimpleBehav.Spec` to call as Scenario() in test classes.
    /// </summary>
    public static GivenToken Scenario(
        [CallerMemberName] string name = "",
        [CallerFilePath] string sourceFile = "") =>
        FromTestMethod(name, null, sourceFile);

    /// <summary>
    /// Captures the calling test method name via CallerMemberName.
    /// </summary>
    public static GivenToken FromTestMethod(
        ISystemUnderTestBuilder? builderOverride = null,
        [CallerMemberName] string name = "",
        [CallerFilePath] string sourceFile = "") =>
        FromTestMethod(name, builderOverride, sourceFile);

    /// <summary>
    /// Explicit-name overload — for use when CallerMemberName must be captured upstream and passed down.
    /// </summary>
    public static GivenToken FromTestMethod(
        string name,
        ISystemUnderTestBuilder? builderOverride = null,
        string sourceFile = "")
    {
        var adapter = builderOverride ?? _defaultAdapter
            ?? throw new InvalidOperationException("[simplebehav] Call Spec.Configure(builder, outDir) before using Spec.FromTestMethod().");
        var variant = ResolveVariant?.Invoke();
        var scenarioName = variant is null ? name : $"{name}[{variant}]";
        var suitePath = ArtifactJson.SuitePath(_projectName, sourceFile);
        var token = new GivenToken(new ScenarioRunner(_outDir!, suitePath, scenarioName, adapter));
        return ConfigureScenario is null ? token : ConfigureScenario(token);
    }

    /// <summary>
    /// Per-scenario overload that supplies both the adapter and the output directory directly,
    /// without touching the static defaults. Use this when each test creates its own adapter
    /// and output path (e.g. a DI-backed page-object runner).
    /// </summary>
    public static GivenToken FromTestMethod(
        string name,
        ISystemUnderTestBuilder builder,
        string outDir,
        string sourceFile = "")
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(outDir);
        var variant = ResolveVariant?.Invoke();
        var scenarioName = variant is null ? name : $"{name}[{variant}]";
        var suitePath = ArtifactJson.SuitePath(_projectName, sourceFile);
        var token = new GivenToken(new ScenarioRunner(outDir, suitePath, scenarioName, builder));
        return ConfigureScenario is null ? token : ConfigureScenario(token);
    }

    public static GivenToken Given(
        ISetup setup,
        [CallerMemberName] string name = "",
        [CallerFilePath] string sourceFile = "") =>
        FromTestMethod(name, null, sourceFile).And(setup);

    public static GivenToken Arrange(
        ISetup setup,
        [CallerMemberName] string name = "",
        [CallerFilePath] string sourceFile = "") =>
        Given(setup, name, sourceFile);
}
