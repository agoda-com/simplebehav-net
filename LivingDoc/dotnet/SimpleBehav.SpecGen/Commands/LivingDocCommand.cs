using SimpleBehav.Reporter;

namespace SimpleBehav.SpecGen.Commands;

internal sealed class LivingDocCommand(
    string testsDir,
    string outDir,
    string? reports,
    string? results,
    string? baseline,
    string[] args) : ICommand
{
    public async Task<int> ExecuteAsync()
    {
        if (!Directory.Exists(testsDir))
        {
            Console.Error.WriteLine($"Tests directory not found: {testsDir}");
            return 1;
        }

        Directory.CreateDirectory(outDir);

        var dryRun = args.Contains("--confluence-dry-run");
        ConfluenceOptions? confluence = null;
        if (args.Contains("--confluence") || dryRun)
        {
            // Validate configuration before doing any work, so a missing secret fails fast.
            try
            {
                string? Option(string name) => CommandLine.Option(args, name);
                confluence = dryRun
                    ? ConfluenceOptions.ForRendering(Option("--confluence-name"), Option("--confluence-scope"), Option("--reports-url"))
                    : ConfluenceOptions.FromEnvironment(Option, Environment.GetEnvironmentVariable, args.Contains("--confluence-prune"));
            }
            catch (ConfluenceConfigException e)
            {
                Console.Error.WriteLine($"Confluence configuration error: {e.Message}");
                return 2;
            }
        }

        var features = await LivingDocEmitter.LoadFeaturesAsync(testsDir);

        if (results is not null)
        {
            // Parallel AC shards each write suite index.html from their own scenarios only;
            // GitLab keeps one index per path. Rebuild from the merged bundles first.
            var rebuilt = await SuiteReporter.RebuildAllAsync(results);
            if (rebuilt > 0)
                Console.WriteLine($"Rebuilt {rebuilt} suite report(s) from merged shard artifacts.");
        }

        var runResults = results is null ? null : await ResultsLoader.LoadAsync(results);
        var manifest = SuiteManifest.Build(features, runResults);

        SuiteDiff? diff = null;
        if (baseline is not null)
        {
            var previous = await SuiteManifest.TryLoadAsync(baseline);
            if (previous is null) Console.WriteLine($"No usable baseline at {baseline}; skipping diff.");
            else diff = SuiteDiff.Compute(previous, manifest);
        }

        await LivingDocEmitter.EmitAsync(features, outDir, reports, manifest, diff);
        Console.WriteLine($"Generated living doc for {features.Count} features / {manifest.Summary.Scenarios} scenarios in {outDir}" +
            (diff is null ? "" : $" ({diff.Changes.Count} changes vs baseline)"));

        if (confluence is null) return 0;

        var pages = ConfluenceRenderer.Render(features, manifest, diff, confluence);
        if (dryRun)
        {
            var confluenceDir = Path.Combine(outDir, "confluence");
            foreach (var page in pages)
            {
                var file = Path.Combine(confluenceDir, page.Key.Replace(':', '_') + ".xml");
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                await File.WriteAllTextAsync(file, page.Body);
            }
            Console.WriteLine($"Dry run: rendered {pages.Count} Confluence pages to {confluenceDir}; nothing was sent.");
            return 0;
        }

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        var publisher = new ConfluencePublisher(new HttpConfluenceClient(http, confluence), confluence, Console.WriteLine);
        try
        {
            var result = await publisher.PublishAsync(pages);
            Console.WriteLine($"Published {result.Pages.Count} pages to Confluence space {result.Space.Key}: " +
                result.Pages.First(p => p.Key == ConfluenceRenderer.IndexKey).Url);
            return 0;
        }
        catch (Exception e) when (e is ConfluenceException or ConfluencePublishException)
        {
            Console.Error.WriteLine($"Confluence publish failed: {e.Message}");
            return 3;
        }
    }
}
