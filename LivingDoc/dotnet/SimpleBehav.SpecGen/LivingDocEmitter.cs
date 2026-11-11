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
    /// or null when there is no run to attach.
    /// </param>
    /// <param name="manifest">Scenarios with their results; written to suite.json.</param>
    /// <param name="diff">Changes against a baseline suite.json, or null when there is no baseline.</param>
    /// <param name="runs">Executed bundles, used to point each scenario at its trace.</param>
    public static async Task EmitAsync(
        IReadOnlyList<Feature> features, string outDir, string? reportsBase, SuiteManifest manifest, SuiteDiff? diff,
        IReadOnlyDictionary<string, List<RunResult>>? runs = null)
    {
        var byId = manifest.Features.SelectMany(f => f.Scenarios).ToDictionary(s => s.Id);

        for (var i = 0; i < features.Count; i++)
        {
            var feature = features[i];
            var dir = Path.Combine(outDir, feature.PagePath);
            Directory.CreateDirectory(dir);
            var depth = feature.PagePath.Count(c => c == '/') + 1;
            var up = string.Concat(Enumerable.Repeat("../", depth));
            await File.WriteAllTextAsync(Path.Combine(dir, "index.html"),
                ViewerPage(feature.Name, feature, [feature], byId, runs, reportsBase, up, new Nav(up, null)));
        }

        Directory.CreateDirectory(outDir);
        await File.WriteAllTextAsync(Path.Combine(outDir, "index.html"),
            ViewerPage("Scenarios", null, features, byId, runs, reportsBase, "", new Nav("", null), manifest, diff));
        // Separate page — does not alter feature pages or suite.json schema.
        await File.WriteAllTextAsync(Path.Combine(outDir, GraphEmitter.FileName), GraphEmitter.Emit(features, reportsBase));
        await File.WriteAllTextAsync(Path.Combine(outDir, "suite.json"),
            System.Text.Json.JsonSerializer.Serialize(manifest, SuiteManifest.Json));
        if (diff is not null)
            await File.WriteAllTextAsync(Path.Combine(outDir, "diff.json"),
                System.Text.Json.JsonSerializer.Serialize(diff, SuiteManifest.Json));
    }

    internal static string Esc(string s) => WebUtility.HtmlEncode(s);

    /// <summary>
    /// One screen: the scenario list, the Given / When / Then spec, and the trace.
    /// Clicking a scenario switches the spec and the trace in place.
    /// </summary>
    private static string ViewerPage(
        string title,
        Feature? single,
        IReadOnlyList<Feature> features,
        IReadOnlyDictionary<string, ScenarioManifest> byId,
        IReadOnlyDictionary<string, List<RunResult>>? runs,
        string? reportsBase,
        string up,
        Nav nav,
        SuiteManifest? manifest = null,
        SuiteDiff? diff = null)
    {
        var sb = new StringBuilder();
        if (manifest is not null)
        {
            var sum = manifest.Summary;
            var hasResults = sum.Passed + sum.Failed > 0;
            sb.AppendLine($"<p class=\"subtitle\">{manifest.Features.Count} feature{(manifest.Features.Count != 1 ? "s" : "")} &middot; {sum.Scenarios} scenario{(sum.Scenarios != 1 ? "s" : "")}" +
                          (hasResults
                              ? $" &middot; <span class=\"pill ok\">{sum.Passed} passed</span>" +
                                (sum.Failed > 0 ? $" <span class=\"pill bad\">{sum.Failed} failed</span>" : "") +
                                (sum.NotRun > 0 ? $" <span class=\"pill muted\">{sum.NotRun} not run</span>" : "")
                              : " &middot; generated from test source") +
                          "</p>");
        }
        else if (single is not null)
        {
            sb.AppendLine($"<p class=\"subtitle\"><code>{Esc(single.PagePath + single.SourceExtension)}</code></p>");
        }

        if (diff is not null) sb.Append(DiffSection(diff));

        sb.AppendLine("<div class=\"screen\">");
        sb.AppendLine("<nav class=\"list\" aria-label=\"Scenarios\">");
        foreach (var feature in features)
        {
            if (single is null && features.Count > 1)
                sb.AppendLine($"<p class=\"group\">{Esc(Humanize(feature.Name.Replace("Tests", "")))}</p>");
            foreach (var scenario in feature.Scenarios)
                sb.AppendLine(Pick(feature, scenario, byId, runs, reportsBase, up, single is null));
        }
        sb.AppendLine("</nav>");

        sb.AppendLine("<div class=\"spec-pane\">");
        foreach (var feature in features)
        {
            foreach (var scenario in feature.Scenarios)
                sb.Append(SpecBlock(feature, scenario, byId[$"{feature.PagePath}/{scenario.MethodName}"], runs, single is null));
        }
        sb.AppendLine("</div>");

        sb.AppendLine("<div class=\"trace-pane\">");
        sb.AppendLine("<p id=\"trace-empty\" class=\"empty\">No trace for this scenario.</p>");
        sb.AppendLine("<iframe id=\"trace-viewer\" title=\"Playwright trace\" hidden></iframe>");
        sb.AppendLine("</div>");
        sb.AppendLine("</div>");
        sb.AppendLine(ViewerScript());
        return Page(single is null ? "Scenarios" : Humanize(title.Replace("Tests", "")), sb.ToString(), nav);
    }

    private static string Pick(
        Feature feature, Scenario scenario, IReadOnlyDictionary<string, ScenarioManifest> byId,
        IReadOnlyDictionary<string, List<RunResult>>? runs, string? reportsBase, string up, bool siteWide)
    {
        var result = byId[$"{feature.PagePath}/{scenario.MethodName}"];
        var id = siteWide ? SpecDomId(feature, scenario) : scenario.MethodName;
        var matches = MatchingRuns(runs, feature.PagePath, scenario.MethodName);
        var trace = Preferred(matches);
        var report = ReportHref(reportsBase, up, feature.PagePath);
        var viewer = ViewerHref(reportsBase, up);
        return $"<a class=\"pick\" href=\"#{Esc(id)}\" data-spec=\"{Esc(id)}\" data-status=\"{Esc(result.Status)}\"" +
               $" data-slug=\"{Esc(trace?.Slug ?? "")}\"" +
               (report is null ? "" : $" data-report=\"{Esc(report)}\"") +
               (viewer is null ? "" : $" data-viewer=\"{Esc(viewer)}\"") +
               $">{Esc(Humanize(scenario.MethodName))} {StatusPill(result)}</a>";
    }

    private static string SpecBlock(Feature feature, Scenario scenario, ScenarioManifest result, IReadOnlyDictionary<string, List<RunResult>>? runs, bool siteWide)
    {
        var id = siteWide ? SpecDomId(feature, scenario) : scenario.MethodName;
        var sb = new StringBuilder();
        sb.AppendLine($"<section class=\"spec\" id=\"{Esc(id)}\">");
        sb.Append($"<h2>{Esc(Humanize(scenario.MethodName))} {StatusPill(result)}");
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

        var matches = MatchingRuns(runs, feature.PagePath, scenario.MethodName);
        if (matches.Count > 1)
        {
            sb.Append("<p class=\"cases\">");
            foreach (var run in matches)
            {
                var label = run.Title.StartsWith(scenario.MethodName + "[", StringComparison.Ordinal) && run.Title.EndsWith(']')
                    ? run.Title[(scenario.MethodName.Length + 1)..^1]
                    : run.Title;
                sb.Append($"<button type=\"button\" class=\"trace-variant\" data-slug=\"{Esc(run.Slug)}\">{Esc(label)}</button> ");
            }
            sb.AppendLine("</p>");
        }

        if (scenario.IsProvesBug)
            sb.AppendLine("<p class=\"note\">Asserts broken behaviour; turns red when the fix ships.</p>");
        sb.AppendLine("</section>");
        return sb.ToString();
    }

    private static string SpecDomId(Feature feature, Scenario scenario) =>
        feature.PagePath.Replace('/', '_') + "_" + scenario.MethodName;

    private static List<RunResult> MatchingRuns(IReadOnlyDictionary<string, List<RunResult>>? runs, string pagePath, string method)
    {
        if (runs is null || !runs.TryGetValue(pagePath, out var list)) return [];
        return list.Where(r => r.Title == method || r.Title.StartsWith(method + "[", StringComparison.Ordinal)).ToList();
    }

    private static RunResult? Preferred(IReadOnlyList<RunResult> matches) =>
        matches.FirstOrDefault(r => r.Status != "pass") ?? matches.FirstOrDefault();

    private static string? ReportHref(string? reportsBase, string up, string pagePath) =>
        reportsBase is null ? null : $"{up}{reportsBase.Trim('/')}/{pagePath}/";

    private static string? ViewerHref(string? reportsBase, string up)
    {
        if (reportsBase is null) return null;
        var trimmed = reportsBase.Trim().Replace('\\', '/').Trim('/');
        var slash = trimmed.LastIndexOf('/');
        var parent = slash < 0 ? "." : trimmed[..slash];
        return up + parent + "/trace-viewer/index.html";
    }

    private static string ViewerScript() => """
        <script>
        const picks = [...document.querySelectorAll(".pick")];
        const specs = [...document.querySelectorAll(".spec")];
        const frame = document.getElementById("trace-viewer");
        const empty = document.getElementById("trace-empty");

        function select(pick) {
          if (!pick) return;
          const id = pick.dataset.spec;
          specs.forEach(spec => spec.classList.toggle("active", spec.id === id));
          picks.forEach(item => item.classList.toggle("selected", item === pick));
          const slug = pick.dataset.slug || "";
          const report = pick.dataset.report || "";
          const viewer = pick.dataset.viewer || "";
          if (!slug || !report || !viewer) {
            frame.hidden = true;
            frame.removeAttribute("src");
            empty.hidden = false;
            return;
          }
          const trace = new URL(report + slug + "/trace.zip", window.location.href);
          const view = new URL(viewer, window.location.href);
          view.searchParams.set("trace", trace.href);
          empty.hidden = true;
          frame.hidden = false;
          if (frame.src !== view.href) frame.src = view.href;
        }

        picks.forEach(pick => pick.addEventListener("click", (event) => {
          if (!document.getElementById(pick.dataset.spec)) return;
          event.preventDefault();
          history.replaceState(null, "", "#" + pick.dataset.spec);
          select(pick);
        }));
        document.querySelectorAll(".trace-variant").forEach(button => button.addEventListener("click", () => {
          const pick = document.querySelector(".pick.selected");
          if (!pick) return;
          pick.dataset.slug = button.dataset.slug;
          select(pick);
        }));

        const hash = decodeURIComponent(location.hash.replace(/^#/, ""));
        const initial = picks.find(pick => pick.dataset.spec === hash)
          || picks.find(pick => pick.dataset.status === "fail")
          || picks[0];
        if (initial) select(initial);
        </script>
        """;

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

    private static string Page(string title, string body, Nav nav) => $$"""
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
  main { margin:0 auto; padding:12px 16px 16px; }
  h2 { font-size:16px; margin:0 0 10px; }
  .subtitle { color:var(--muted); font-size:13px; margin:0 0 12px; }
  a { color:inherit; text-decoration:none; }
  code { font-family:"SFMono-Regular",Consolas,monospace; font-size:12px; background:var(--alt); padding:1px 5px; border-radius:4px; }
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
  pre.error { background:#ffebe9; border:1px solid #cf222e; border-radius:6px; padding:8px 10px; font-size:12px; white-space:pre-wrap; margin:8px 0 0; }
  .changes { border:1px solid var(--border); border-radius:6px; padding:12px 16px; margin-bottom:12px; background:var(--alt); }
  .changes ul { list-style:none; margin:0; padding:0; } .changes li { padding:3px 0; }
  .cases, .note, .empty { color:var(--muted); font-size:12px; margin:8px 0 0; }
  .trace-variant { margin:8px 6px 0 0; font:inherit; font-size:12px; padding:2px 8px; border:1px solid var(--border); border-radius:20px; background:#fff; cursor:pointer; }
  .screen { display:grid; grid-template-columns:240px minmax(260px, 360px) minmax(0, 1fr); gap:12px; height:calc(100vh - 58px); }
  .list { overflow:auto; }
  .group { margin:12px 0 4px; font-size:12px; color:var(--muted); }
  .pick { display:block; padding:8px 10px; border:1px solid transparent; border-radius:6px; margin-bottom:4px; }
  .pick:hover { background:var(--alt); }
  .pick.selected { border-color:var(--accent); background:var(--info-bg); }
  .spec { display:none; }
  .spec.active { display:block; }
  .spec-pane, .trace-pane { min-width:0; }
  .spec-pane { overflow:auto; border:1px solid var(--border); border-radius:6px; padding:12px 14px; }
  .trace-pane { display:flex; flex-direction:column; }
  #trace-viewer { flex:1; width:100%; border:1px solid var(--border); border-radius:6px; background:var(--alt); }
  {{NavCss}}
  @media (max-width: 900px) {
    .screen { grid-template-columns:1fr; height:auto; }
    #trace-viewer { height:70vh; }
  }
</style>
</head>
<body>
<header class="topbar"><a class="brand" href="{{Esc(nav.Root)}}index.html">Scenarios</a></header>
<main>
{{body}}
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
