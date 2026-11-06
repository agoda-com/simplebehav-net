using System.Text.Json;

namespace SimpleBehav.Reporter;

/// <summary>
/// Writes the suite page for one test file. Called after every scenario so the
/// report is complete once the last scenario in the file finishes.
/// </summary>
public static class SuiteReporter
{
    public static async Task WriteAsync(string suiteDir, string suiteTitle, string traceViewerBase)
    {
        var scenarios = new List<SuiteScenario>();
        foreach (var dir in Directory.GetDirectories(suiteDir))
        {
            var bundlePath = Path.Combine(dir, "bundle.json");
            if (!File.Exists(bundlePath)) continue;

            var bundle = JsonSerializer.Deserialize<ArtifactBundle>(
                await File.ReadAllTextAsync(bundlePath), ArtifactJson.Options);
            if (bundle is not null)
                scenarios.Add(new SuiteScenario(Path.GetFileName(dir), bundle));
        }

        var ordered = scenarios.OrderBy(s => s.Bundle.StartedAt).ToList();
        await File.WriteAllTextAsync(
            Path.Combine(suiteDir, "index.html"),
            DocsGenerator.Generate(suiteTitle, ordered, traceViewerBase));
    }

    /// <summary>
    /// Rebuild every suite <c>index.html</c> under an artifacts root after CI merges
    /// parallel shards. Each shard writes an index from only its own scenarios; the
    /// last writer wins on the shared path, so without a rebuild the published report
    /// shows a single browser/device (e.g. webkit-mobile) even when other bundles exist.
    /// </summary>
    public static async Task<int> RebuildAllAsync(string artifactsRoot)
    {
        if (!Directory.Exists(artifactsRoot)) return 0;

        var rebuilt = 0;
        var root = Path.GetFullPath(artifactsRoot);
        foreach (var suiteDir in DiscoverSuiteDirs(root))
        {
            var relative = Path.GetRelativePath(root, suiteDir).Replace('\\', '/');
            var depth = relative.Count(c => c == '/') + 1; // suite path segments under artifacts root
            var traceViewerBase = string.Concat(Enumerable.Repeat("../", depth)) + "trace-viewer/index.html";
            await WriteAsync(suiteDir, Path.GetFileName(suiteDir), traceViewerBase);
            rebuilt++;
        }
        return rebuilt;
    }

    private static IEnumerable<string> DiscoverSuiteDirs(string root)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var bundle in Directory.EnumerateFiles(root, "bundle.json", SearchOption.AllDirectories))
        {
            var scenarioDir = Path.GetDirectoryName(bundle);
            var suiteDir = scenarioDir is null ? null : Path.GetDirectoryName(scenarioDir);
            if (suiteDir is null || !seen.Add(suiteDir)) continue;
            yield return suiteDir;
        }
    }
}
