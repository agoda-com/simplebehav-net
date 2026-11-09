using System.Text.Json;
using System.Text.Json.Serialization;

namespace SimpleBehav.SpecGen;

public static class Status
{
    public const string Pass = "pass";
    public const string Fail = "fail";
    public const string NotRun = "notRun";
}

public sealed record StepManifest(string Type, string Label);

public sealed record ScenarioManifest(
    string Id,
    string Title,
    bool ProvesBug,
    IReadOnlyList<string>? Cases,
    string Status,
    long? DurationMs,
    string? Error,
    IReadOnlyList<StepManifest> Steps);

public sealed record FeatureManifest(string Feature, string Path, IReadOnlyList<ScenarioManifest> Scenarios);

public sealed record SuiteSummary(int Scenarios, int Passed, int Failed, int NotRun);

/// <summary>
/// suite.json — the machine-readable form of the living documentation: every scenario with a stable id,
/// its steps, and (when a test run is supplied) its result. Input for diffs and for tooling/agents.
/// </summary>
public sealed record SuiteManifest(int SchemaVersion, SuiteSummary Summary, IReadOnlyList<FeatureManifest> Features)
{
    public const int CurrentSchemaVersion = 1;

    internal static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static SuiteManifest Build(IReadOnlyList<Feature> features, IReadOnlyDictionary<string, List<RunResult>>? results)
    {
        var built = features.Select(f => new FeatureManifest(f.Name, f.PagePath,
            f.Scenarios.Select(s =>
            {
                var runs = results is not null && results.TryGetValue(f.PagePath, out var r) ? r : null;
                var (status, duration, error) = Aggregate(runs, s.MethodName);
                return new ScenarioManifest(
                    $"{f.PagePath}/{s.MethodName}",
                    LivingDocEmitter.Humanize(s.MethodName),
                    s.IsProvesBug,
                    s.Cases,
                    status,
                    duration,
                    error,
                    s.Steps.Select(st => new StepManifest(st.Type, st.Label)).ToList());
            }).ToList())).ToList();

        var all = built.SelectMany(f => f.Scenarios).ToList();
        return new SuiteManifest(CurrentSchemaVersion,
            new SuiteSummary(
                all.Count,
                all.Count(s => s.Status == Status.Pass),
                all.Count(s => s.Status == Status.Fail),
                all.Count(s => s.Status == Status.NotRun)),
            built);
    }

    /// <summary>Loads a previously published suite.json; null when missing, unreadable or from another schema.</summary>
    public static async Task<SuiteManifest?> TryLoadAsync(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var manifest = JsonSerializer.Deserialize<SuiteManifest>(await File.ReadAllTextAsync(path), Json);
            return manifest?.SchemaVersion == CurrentSchemaVersion ? manifest : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // A scenario with [TestCase] variants has one bundle per variant, titled "Method[arg]".
    private static (string Status, long? Duration, string? Error) Aggregate(List<RunResult>? runs, string method)
    {
        var matches = runs?.Where(r => r.Title == method || r.Title.StartsWith(method + "[", StringComparison.Ordinal)).ToList();
        if (matches is null || matches.Count == 0) return (Status.NotRun, null, null);

        var failed = matches.Where(r => r.Status != "pass").ToList();
        var duration = matches.Sum(r => r.DurationMs);
        if (failed.Count == 0) return (Status.Pass, duration, null);

        var error = failed.Select(r => r.Error is null ? null : matches.Count > 1 ? $"{r.Title}: {r.Error}" : r.Error)
            .FirstOrDefault(e => e is not null);
        return (Status.Fail, duration, error);
    }
}

/// <summary>One executed scenario read back from a bundle.json.</summary>
public sealed record RunResult(string Title, string Status, long DurationMs, string? Error, string Slug);

public static class ResultsLoader
{
    /// <summary>
    /// Reads every <c>&lt;assembly&gt;/&lt;folders&gt;/&lt;TestFile&gt;/&lt;scenario&gt;/bundle.json</c> under
    /// <paramref name="dir"/>, keyed by <c>&lt;folders&gt;/&lt;TestFile&gt;</c> (the assembly segment is dropped so
    /// keys line up with <see cref="Feature.PagePath"/>).
    /// </summary>
    public static async Task<Dictionary<string, List<RunResult>>> LoadAsync(string dir)
    {
        var results = new Dictionary<string, List<RunResult>>();
        if (!Directory.Exists(dir)) return results;

        foreach (var file in Directory.EnumerateFiles(dir, "bundle.json", SearchOption.AllDirectories))
        {
            var segments = Path.GetRelativePath(dir, file).Replace('\\', '/').Split('/');
            if (segments.Length < 4) continue; // assembly / file / scenario / bundle.json at the least

            try
            {
                using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(file));
                var root = doc.RootElement;
                var error = root.TryGetProperty("steps", out var steps)
                    ? steps.EnumerateArray()
                        .Select(s => s.TryGetProperty("error", out var e) ? e.GetString() : null)
                        .FirstOrDefault(e => e is not null)
                    : null;

                var key = string.Join('/', segments[1..^2]);
                if (!results.TryGetValue(key, out var list)) results[key] = list = [];
                list.Add(new RunResult(
                    root.GetProperty("title").GetString() ?? "",
                    root.GetProperty("status").GetString() ?? "fail",
                    root.TryGetProperty("durationMs", out var d) ? d.GetInt64() : 0,
                    error,
                    segments[^2]));
            }
            catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
            {
                // A truncated bundle (interrupted run) shouldn't take the whole site down.
            }
        }
        return results;
    }
}

public sealed record ScenarioChange(string Id, string Title, string Kind, string? From = null, string? To = null);

public sealed record SuiteDiff(IReadOnlyList<ScenarioChange> Changes)
{
    public const string Added = "added";
    public const string Removed = "removed";
    public const string StatusChanged = "statusChanged";
    public const string StepsChanged = "stepsChanged";

    public static SuiteDiff Compute(SuiteManifest baseline, SuiteManifest current)
    {
        var before = baseline.Features.SelectMany(f => f.Scenarios).ToDictionary(s => s.Id);
        var after = current.Features.SelectMany(f => f.Scenarios).ToDictionary(s => s.Id);
        var changes = new List<ScenarioChange>();

        foreach (var (id, s) in after)
        {
            if (!before.TryGetValue(id, out var old))
            {
                changes.Add(new ScenarioChange(id, s.Title, Added));
                continue;
            }
            if (old.Status != s.Status)
                changes.Add(new ScenarioChange(id, s.Title, StatusChanged, old.Status, s.Status));
            if (!old.Steps.SequenceEqual(s.Steps))
                changes.Add(new ScenarioChange(id, s.Title, StepsChanged));
        }
        changes.AddRange(before.Where(kv => !after.ContainsKey(kv.Key))
            .Select(kv => new ScenarioChange(kv.Key, kv.Value.Title, Removed)));

        return new SuiteDiff(changes.OrderBy(c => c.Id, StringComparer.Ordinal).ToList());
    }
}
