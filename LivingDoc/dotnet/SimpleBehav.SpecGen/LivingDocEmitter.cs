using System.Net;
using System.Text;

namespace SimpleBehav.SpecGen;

/// <summary>One test file and the scenarios parsed from it.</summary>
public sealed record Feature(
    string Name,
    string RelativeDir,
    IReadOnlyList<Scenario> Scenarios,
    string SourceExtension = ".cs")
{
    /// <summary>Site-relative directory of this feature's page, mirroring the test source layout.</summary>
    public string PagePath => RelativeDir.Length == 0 ? Name : $"{RelativeDir}/{Name}";
}

/// <summary>
/// Emits the living documentation site: one HTML page per test file plus an index,
/// derived from C#, Kotlin, and TypeScript test source — no test run needed.
/// </summary>
public static class LivingDocEmitter
{
    /// <summary>
    /// Parses every test file under <paramref name="testsDir"/>. Tests without a Given/When/Then chain
    /// (plain unit tests) aren't behaviour specs, so they and files containing only them are skipped.
    /// </summary>
    public static async Task<IReadOnlyList<Feature>> LoadFeaturesAsync(string testsDir)
    {
        var features = new List<Feature>();
        foreach (var file in ScenarioFiles(testsDir))
        {
            var relative = Path.GetRelativePath(testsDir, file).Replace('\\', '/');
            var scenarios = SpecParser.ParseFile(file, await File.ReadAllTextAsync(file))
                .Where(s => s.Steps.Count > 0).ToList();
            if (scenarios.Count == 0) continue;

            var dir = Path.GetDirectoryName(relative)?.Replace('\\', '/') ?? "";
            features.Add(new Feature(
                Path.GetFileNameWithoutExtension(file),
                dir,
                scenarios,
                Path.GetExtension(file)));
        }
        return features;
    }

    private static IEnumerable<string> ScenarioFiles(string testsDir)
    {
        string[] patterns = ["*.cs", "*.kt", "*.ts", "*.tsx"];
        return patterns
            .SelectMany(pattern => Directory.EnumerateFiles(testsDir, pattern, SearchOption.AllDirectories))
            .Where(file => !file.EndsWith(".d.ts", StringComparison.OrdinalIgnoreCase))
            .Select(file => (File: file, Relative: Path.GetRelativePath(testsDir, file).Replace('\\', '/')))
            .Where(file => !IsGeneratedOutput(file.Relative))
            .OrderBy(file => file.Relative, StringComparer.Ordinal)
            .Select(file => file.File);
    }

    private static bool IsGeneratedOutput(string relative) =>
        relative.Split('/').Any(part => part is "bin" or "obj" or "node_modules" or "dist" or "build" or ".gradle" or ".kotlin");

    /// <param name="reportsBase">
    /// Relative path from the site root to the test run reports (<c>&lt;reportsBase&gt;/&lt;folders&gt;/&lt;TestFile&gt;/</c>),
    /// or null to omit "run report" links.
    /// </param>
    /// <param name="manifest">Scenarios with their results; written to suite.json.</param>
    /// <param name="diff">Changes against a baseline suite.json, or null when there is no baseline.</param>
    public static async Task EmitAsync(
        IReadOnlyList<Feature> features, string outDir, string? reportsBase, SuiteManifest manifest, SuiteDiff? diff)
    {
        var byId = manifest.Features.SelectMany(f => f.Scenarios).ToDictionary(s => s.Id);

        for (var i = 0; i < features.Count; i++)
        {
            var feature = features[i];
            var dir = Path.Combine(outDir, feature.PagePath);
            Directory.CreateDirectory(dir);
            var depth = feature.PagePath.Count(c => c == '/') + 1;
            var up = string.Concat(Enumerable.Repeat("../", depth));
            var reportHref = reportsBase is null ? null : $"{up}{reportsBase.Trim('/')}/{feature.PagePath}/";
            var nav = new Nav(up, reportsBase is null ? null : $"{up}{reportsBase.Trim('/')}/");
            await File.WriteAllTextAsync(Path.Combine(dir, "index.html"),
                FeaturePage(feature, nav, reportHref, byId, i > 0 ? features[i - 1] : null, i < features.Count - 1 ? features[i + 1] : null));
        }

        Directory.CreateDirectory(outDir);
        await File.WriteAllTextAsync(Path.Combine(outDir, "index.html"), IndexPage(features, manifest, diff, byId, new Nav("", reportsBase is null ? null : $"{reportsBase.Trim('/')}/")));
        // Separate page — does not alter feature pages or suite.json schema.
        await File.WriteAllTextAsync(Path.Combine(outDir, GraphEmitter.FileName), GraphEmitter.Emit(features, reportsBase));
        await File.WriteAllTextAsync(Path.Combine(outDir, "suite.json"),
            System.Text.Json.JsonSerializer.Serialize(manifest, SuiteManifest.Json));
        if (diff is not null)
            await File.WriteAllTextAsync(Path.Combine(outDir, "diff.json"),
                System.Text.Json.JsonSerializer.Serialize(diff, SuiteManifest.Json));
    }

    internal static string Esc(string s) => WebUtility.HtmlEncode(s);

    private static string FeaturePage(
        Feature feature, Nav nav, string? reportHref, IReadOnlyDictionary<string, ScenarioManifest> byId,
        Feature? prev, Feature? next)
    {
        var sb = new StringBuilder();
        var crumbs = new List<(string Label, string? Href)> { ("Features", nav.Root + "index.html") };
        if (feature.RelativeDir.Length > 0) crumbs.Add((feature.RelativeDir, nav.Root + "index.html#" + GroupAnchor(feature.RelativeDir)));
        crumbs.Add((Humanize(feature.Name.Replace("Tests", "")), null));
        sb.AppendLine(Breadcrumbs(crumbs));
        sb.AppendLine($"<h1>{Esc(Humanize(feature.Name.Replace("Tests", "")))}</h1>");
        sb.AppendLine($"<p class=\"subtitle\">{feature.Scenarios.Count} scenario{(feature.Scenarios.Count != 1 ? "s" : "")}" +
                      $" &middot; <code>{Esc(feature.PagePath + feature.SourceExtension)}</code>" +
                      (reportHref is null ? "" : $" &middot; <a href=\"{Esc(reportHref)}\">Run report &amp; traces</a>") +
                      "</p>");

        foreach (var scenario in feature.Scenarios)
        {
            var result = byId[$"{feature.PagePath}/{scenario.MethodName}"];
            sb.AppendLine($"<section class=\"scenario {Tone(result.Status)}\" id=\"{Esc(scenario.MethodName)}\">");
            sb.Append($"<h2><a class=\"anchor\" href=\"#{Esc(scenario.MethodName)}\">{Esc(Humanize(scenario.MethodName))}</a> {StatusPill(result)}");
            if (scenario.IsProvesBug) sb.Append(" <span class=\"pill warn\">proves bug</span>");
            sb.AppendLine("</h2>");

            sb.AppendLine("<ol class=\"steps\">");
            foreach (var step in scenario.Steps)
                sb.AppendLine($"<li><span class=\"kw {Esc(step.Type.ToLowerInvariant())}\">{Esc(step.Type)}</span> {StepLabel(step.Label)}</li>");
            sb.AppendLine("</ol>");

            if (result.Error is not null)
                sb.AppendLine($"<pre class=\"error\">{Esc(result.Error)}</pre>");

            if (scenario.Cases is { Count: > 0 })
                sb.AppendLine($"<p class=\"cases\">Cases: {string.Join(" ", scenario.Cases.Select(c => $"<code>{Esc(c)}</code>"))}</p>");

            if (scenario.IsProvesBug)
                sb.AppendLine("<p class=\"note\">Asserts broken behaviour; turns red when the fix ships.</p>");
            sb.AppendLine("</section>");
        }

        sb.AppendLine("<nav class=\"pager\">");
        sb.AppendLine(prev is null ? "<span></span>" : $"<a href=\"{nav.Root}{Esc(prev.PagePath)}/\">&larr; {Esc(Humanize(prev.Name.Replace("Tests", "")))}</a>");
        sb.AppendLine(next is null ? "<span></span>" : $"<a href=\"{nav.Root}{Esc(next.PagePath)}/\">{Esc(Humanize(next.Name.Replace("Tests", "")))} &rarr;</a>");
        sb.AppendLine("</nav>");
        return Page(Humanize(feature.Name), sb.ToString(), nav, NavSection.Features);
    }

    private static string IndexPage(
        IReadOnlyList<Feature> features, SuiteManifest manifest, SuiteDiff? diff,
        IReadOnlyDictionary<string, ScenarioManifest> byId, Nav nav)
    {
        var sum = manifest.Summary;
        var hasResults = sum.Passed + sum.Failed > 0;
        var sb = new StringBuilder();
        sb.AppendLine("<h1>Living documentation</h1>");
        sb.AppendLine($"<p class=\"subtitle\">{features.Count} feature{(features.Count != 1 ? "s" : "")} &middot; {sum.Scenarios} scenario{(sum.Scenarios != 1 ? "s" : "")}" +
                      (hasResults
                          ? $" &middot; <span class=\"pill ok\">{sum.Passed} passed</span>" +
                            (sum.Failed > 0 ? $" <span class=\"pill bad\">{sum.Failed} failed</span>" : "") +
                            (sum.NotRun > 0 ? $" <span class=\"pill muted\">{sum.NotRun} not run</span>" : "")
                          : " &middot; generated from test source") +
                      "</p>");

        if (diff is not null) sb.Append(DiffSection(diff));

        foreach (var group in features.GroupBy(f => f.RelativeDir))
        {
            if (group.Key.Length > 0) sb.AppendLine($"<h2 class=\"group\" id=\"{GroupAnchor(group.Key)}\">{Esc(group.Key)}</h2>");
            sb.AppendLine("<ul class=\"features\">");
            foreach (var f in group)
            {
                sb.AppendLine($"<li><a href=\"{Esc(f.PagePath)}/\">{Esc(Humanize(f.Name.Replace("Tests", "")))}</a>" +
                              (nav.Reports is null ? "" : $" <a class=\"aux\" href=\"{Esc(nav.Reports)}{Esc(f.PagePath)}/\">traces</a>"));
                sb.AppendLine("<ul>");
                foreach (var s in f.Scenarios)
                    sb.AppendLine($"<li><a href=\"{Esc(f.PagePath)}/#{Esc(s.MethodName)}\">{Esc(Humanize(s.MethodName))}</a> {StatusPill(byId[$"{f.PagePath}/{s.MethodName}"])}</li>");
                sb.AppendLine("</ul></li>");
            }
            sb.AppendLine("</ul>");
        }
        return Page("Living documentation", sb.ToString(), nav, NavSection.Features);
    }

    private static string DiffSection(SuiteDiff diff)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<section class=\"changes\">");
        sb.AppendLine("<h2>Changes since baseline</h2>");
        if (diff.Changes.Count == 0)
        {
            sb.AppendLine("<p class=\"note\">No changes to scenarios, steps or results.</p>");
        }
        else
        {
            sb.AppendLine("<ul>");
            foreach (var c in diff.Changes)
            {
                var (label, tone) = c.Kind switch
                {
                    SuiteDiff.Added => ("added", "ok"),
                    SuiteDiff.Removed => ("removed", "muted"),
                    SuiteDiff.StepsChanged => ("steps changed", "warn"),
                    _ => (c.To == Status.Fail ? "now failing" : c.To == Status.Pass ? "now passing" : "result changed",
                          c.To == Status.Fail ? "bad" : "ok"),
                };
                sb.AppendLine($"<li><span class=\"pill {tone}\">{label}</span> {Esc(c.Title)} <code>{Esc(c.Id)}</code></li>");
            }
            sb.AppendLine("</ul>");
        }
        sb.AppendLine("</section>");
        return sb.ToString();
    }

    private static string Tone(string status) => status switch
    {
        Status.Pass => "pass", Status.Fail => "fail", _ => "",
    };

    private static string StatusPill(ScenarioManifest s) => s.Status switch
    {
        Status.Pass => $"<span class=\"pill ok\">PASS</span> <span class=\"dur\">{s.DurationMs}ms</span>",
        Status.Fail => $"<span class=\"pill bad\">FAIL</span> <span class=\"dur\">{s.DurationMs}ms</span>",
        _ => "<span class=\"pill muted\">NOT RUN</span>",
    };

    // Quoted string literals read as prose; anything else (e.g. Pages.Checkout) is a code reference.
    private static string StepLabel(string label) =>
        label.Contains('.') && !label.Contains(' ') ? $"<code>{Esc(label)}</code>" : Esc(label);

    internal enum NavSection { Features, Graph }

    /// <param name="Root">Relative path from the current page to the site root ("" or "../../").</param>
    /// <param name="Reports">Relative path from the current page to the run reports, or null.</param>
    internal sealed record Nav(string Root, string? Reports);

    internal static string GroupAnchor(string group) =>
        new(group.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-').ToArray());

    internal static string Breadcrumbs(IEnumerable<(string Label, string? Href)> crumbs) =>
        "<nav class=\"crumbs\" aria-label=\"Breadcrumb\">" +
        string.Join(" <span>/</span> ", crumbs.Select(c =>
            c.Href is null ? $"<strong>{Esc(c.Label)}</strong>" : $"<a href=\"{Esc(c.Href)}\">{Esc(c.Label)}</a>")) +
        "</nav>";

    /// <summary>Site-wide top bar shared by every generated page, so each page links to every other section.</summary>
    internal static string TopBar(Nav nav, NavSection current)
    {
        string Item(string label, string href, bool active) =>
            $"<a href=\"{Esc(href)}\"{(active ? " class=\"active\" aria-current=\"page\"" : "")}>{label}</a>";
        return "<header class=\"topbar\"><span class=\"brand\">SimpleBehav</span>" +
               Item("Features", nav.Root + "index.html", current == NavSection.Features) +
               Item("Page graph", nav.Root + GraphEmitter.FileName, current == NavSection.Graph) +
               (nav.Reports is null ? "" : Item("Run reports", nav.Reports, false)) +
               "</header>";
    }

    internal const string NavCss = """
  .topbar { display:flex; gap:20px; align-items:center; padding:10px 24px; border-bottom:1px solid var(--border); background:var(--alt); font-size:13px; }
  .topbar .brand { font-weight:700; margin-right:8px; }
  .topbar a { color:var(--muted); padding:2px 0; border-bottom:2px solid transparent; }
  .topbar a.active { color:var(--text); font-weight:600; border-bottom-color:var(--accent); }
  .crumbs { font-size:12px; color:var(--muted); margin-bottom:10px; } .crumbs span { margin:0 4px; }
  .pager { display:flex; justify-content:space-between; margin-top:20px; padding-top:12px; border-top:1px solid var(--border); }
  a.aux { font-weight:400; font-size:12px; margin-left:8px; }
  a.anchor { color:inherit; }
""";

    private static string Page(string title, string body, Nav nav, NavSection section) => $$"""
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<title>{{Esc(title)}} &mdash; SimpleBehav</title>
<style>
  :root { --border:#d0d7de; --alt:#f6f8fa; --text:#1f2328; --muted:#59636e; --accent:#0969da;
          --info:#0969da; --info-bg:#ddf4ff; --ok:#1a7f37; --ok-bg:#dafbe1; --warn:#9a6700; --warn-bg:#fff8c5; }
  *,*::before,*::after { box-sizing:border-box; }
  body { font-family:-apple-system,BlinkMacSystemFont,"Segoe UI",Helvetica,Arial,sans-serif; font-size:14px;
         line-height:1.55; color:var(--text); margin:0; }
  main { max-width:860px; margin:0 auto; padding:24px 16px; }
  h1 { font-size:22px; margin:0 0 4px; }
  h2 { font-size:15px; margin:0 0 8px; }
  h2.group { margin-top:24px; font-family:monospace; color:var(--muted); font-weight:500; }
  .subtitle { color:var(--muted); font-size:13px; margin:0 0 20px; padding-bottom:16px; border-bottom:1px solid var(--border); }
  a { color:var(--accent); text-decoration:none; } a:hover { text-decoration:underline; }
  code { font-family:"SFMono-Regular",Consolas,monospace; font-size:12px; background:var(--alt); padding:1px 5px; border-radius:4px; }
  .scenario { border:1px solid var(--border); border-radius:6px; padding:14px 16px; margin-bottom:14px; }
  .steps { list-style:none; margin:0; padding:0; }
  .steps li { padding:3px 0; }
  .kw { display:inline-block; min-width:52px; font-weight:700; font-size:12px; text-transform:uppercase; color:var(--info); }
  .kw.then, .kw.and { color:var(--ok); }
  .pill { font-size:11px; font-weight:600; padding:1px 8px; border-radius:20px; vertical-align:middle; }
  .pill.warn { background:var(--warn-bg); color:var(--warn); }
  .pill.ok { background:var(--ok-bg); color:var(--ok); }
  .pill.bad { background:#ffebe9; color:#cf222e; }
  .pill.muted { background:var(--alt); color:var(--muted); }
  .dur { font-size:11px; color:var(--muted); font-family:monospace; font-weight:400; }
  .scenario.pass { border-left:4px solid var(--ok); } .scenario.fail { border-left:4px solid #cf222e; }
  pre.error { background:#ffebe9; border:1px solid #cf222e; border-radius:6px; padding:8px 10px; font-size:12px; white-space:pre-wrap; margin:8px 0 0; }
  .changes { border:1px solid var(--border); border-radius:6px; padding:12px 16px; margin-bottom:20px; background:var(--alt); }
  .changes ul { list-style:none; margin:0; padding:0; } .changes li { padding:3px 0; }
  .cases, .note { color:var(--muted); font-size:12px; margin:8px 0 0; }
  ul.features { list-style:none; padding:0; margin:0; }
  ul.features > li { border:1px solid var(--border); border-radius:6px; padding:10px 14px; margin-bottom:10px; font-weight:600; }
  {{NavCss}}
  ul.features ul { font-weight:400; color:var(--muted); font-size:13px; margin:6px 0 0; padding-left:18px; }
</style>
</head>
<body>
{{TopBar(nav, section)}}
<main>
{{body}}
<p class="subtitle" style="border:0;margin-top:24px">Generated by <strong>simplebehav-gen</strong> from test source.</p>
</main></body>
</html>
""";

    internal static string Humanize(string name) =>
        string.Join(" ", name.Split('_').Where(p => p.Length > 0).SelectMany(SplitCamelCase));

    private static IEnumerable<string> SplitCamelCase(string s)
    {
        var current = new StringBuilder();
        foreach (var c in s)
        {
            if (char.IsUpper(c) && current.Length > 0)
            {
                yield return current.ToString();
                current.Clear();
            }
            current.Append(c);
        }
        if (current.Length > 0) yield return current.ToString();
    }
}
