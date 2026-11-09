using System.Net;
using System.Text;

namespace SimpleBehav.SpecGen;

/// <summary>One page to publish. <see cref="Key"/> is stable across runs; <see cref="Title"/> is what Confluence knows it by.</summary>
public sealed record ConfluencePage(string Key, string Title, string Body, bool IsRoot = false);

/// <summary>
/// Renders the living documentation as Confluence storage format (XHTML + <c>ac:</c> macros) — the HTML site's
/// CSS and JavaScript (including the Mermaid graph) don't survive Confluence, so everything here is tables,
/// headings, status lozenges and native page links. Page titles are the link targets, and all pages live in
/// one space, so a link never needs a page id.
/// </summary>
public static class ConfluenceRenderer
{
    public const string GraphKey = "graph";
    public const string IndexKey = "index";

    public static IReadOnlyList<ConfluencePage> Render(
        IReadOnlyList<Feature> features, SuiteManifest manifest, SuiteDiff? diff, ConfluenceOptions options)
    {
        var byId = manifest.Features.SelectMany(f => f.Scenarios).ToDictionary(s => s.Id);
        var titles = new Titles(features, options);
        var pages = new List<ConfluencePage>
        {
            new(IndexKey, titles.Root, Index(features, manifest, diff, byId, titles, options), IsRoot: true),
            new(GraphKey, titles.Graph, Graph(features, byId, titles, options)),
        };
        for (var i = 0; i < features.Count; i++)
        {
            var f = features[i];
            pages.Add(new ConfluencePage(FeatureKey(f), titles.Of(f),
                FeaturePage(f, byId, titles, options, i > 0 ? features[i - 1] : null, i < features.Count - 1 ? features[i + 1] : null)));
        }
        return pages;
    }

    public static string FeatureKey(Feature f) => "feature:" + f.PagePath;

    /// <summary>Page titles within a space must be unique, and Confluence rejects some characters in them.</summary>
    private sealed class Titles
    {
        private readonly Dictionary<string, string> _features = new();
        public string Root { get; }
        public string Graph => $"{Root} - Page graph";

        public Titles(IReadOnlyList<Feature> features, ConfluenceOptions options)
        {
            Root = Clean(options.RootTitle);
            foreach (var f in features)
            {
                var label = (f.RelativeDir.Length == 0 ? "" : f.RelativeDir.Replace("/", " - ") + " - ") + FeatureLabel(f);
                var title = $"{Root} - {Clean(label)}";
                if (title == Graph || _features.ContainsValue(title))
                    throw new InvalidOperationException($"Two pages would be titled '{title}'; rename a test file or folder.");
                _features[f.PagePath] = title;
            }
        }

        public string Of(Feature f) => _features[f.PagePath];
        public string Of(string pagePath) => _features[pagePath];
    }

    // Confluence page titles can't contain : @ / \ | ^ and are limited to 255 characters.
    private static string Clean(string title)
    {
        var cleaned = string.Concat(title.Select(c => c is ':' or '@' or '/' or '\\' or '|' or '^' ? ' ' : c));
        cleaned = string.Join(' ', cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return cleaned.Length <= 255 ? cleaned : cleaned[..255].TrimEnd();
    }

    private static string FeatureLabel(Feature f) => LivingDocEmitter.Humanize(f.Name.Replace("Tests", ""));

    // ---- pages ----

    private static string FeaturePage(
        Feature feature, IReadOnlyDictionary<string, ScenarioManifest> byId, Titles titles, ConfluenceOptions o,
        Feature? prev, Feature? next)
    {
        var sb = new StringBuilder();
        sb.Append(Nav(titles, o, ReportsLink(o, feature, "Run report & traces")));
        sb.Append(GeneratedNotice());
        sb.Append($"<p>{feature.Scenarios.Count} scenario{(feature.Scenarios.Count != 1 ? "s" : "")} · <code>{Esc(feature.PagePath + feature.SourceExtension)}</code></p>");
        sb.Append(Macro("toc"));

        foreach (var scenario in feature.Scenarios)
        {
            var result = byId[$"{feature.PagePath}/{scenario.MethodName}"];
            sb.Append(Anchor(scenario.MethodName));
            sb.Append($"<h2>{Esc(LivingDocEmitter.Humanize(scenario.MethodName))} {ResultLozenge(result)}");
            if (scenario.IsProvesBug) sb.Append(' ').Append(Lozenge("Yellow", "PROVES BUG"));
            sb.Append("</h2>");

            sb.Append("<table><tbody>");
            foreach (var step in scenario.Steps)
                sb.Append($"<tr><th>{Esc(step.Type)}</th><td>{StepLabel(step.Label)}</td></tr>");
            sb.Append("</tbody></table>");

            if (result.Error is not null) sb.Append(Code(result.Error));
            if (scenario.Cases is { Count: > 0 })
                sb.Append($"<p>Cases: {string.Join(" ", scenario.Cases.Select(c => $"<code>{Esc(c)}</code>"))}</p>");
            if (scenario.IsProvesBug)
                sb.Append("<p><em>Asserts broken behaviour; turns red when the fix ships.</em></p>");
        }

        sb.Append("<hr/><p>");
        sb.Append(prev is null ? "" : "← " + PageLink(titles.Of(prev), FeatureLabel(prev)));
        sb.Append(prev is not null && next is not null ? " · " : "");
        sb.Append(next is null ? "" : PageLink(titles.Of(next), FeatureLabel(next)) + " →");
        sb.Append("</p>");
        return sb.ToString();
    }

    private static string Index(
        IReadOnlyList<Feature> features, SuiteManifest manifest, SuiteDiff? diff,
        IReadOnlyDictionary<string, ScenarioManifest> byId, Titles titles, ConfluenceOptions o)
    {
        var sum = manifest.Summary;
        var hasResults = sum.Passed + sum.Failed > 0;
        var sb = new StringBuilder();
        sb.Append(Nav(titles, o, null));
        sb.Append(GeneratedNotice());
        sb.Append($"<p>{features.Count} feature{(features.Count != 1 ? "s" : "")} · {sum.Scenarios} scenario{(sum.Scenarios != 1 ? "s" : "")} · ");
        if (!hasResults) sb.Append("generated from test source");
        else
        {
            sb.Append(Lozenge("Green", $"{sum.Passed} passed"));
            if (sum.Failed > 0) sb.Append(' ').Append(Lozenge("Red", $"{sum.Failed} failed"));
            if (sum.NotRun > 0) sb.Append(' ').Append(Lozenge("Grey", $"{sum.NotRun} not run"));
        }
        sb.Append("</p>");
        if (o.ReportsUrl is not null)
            sb.Append($"<p>{ExternalLink(o.ReportsUrl.TrimEnd('/') + "/", "All run reports & traces")}</p>");

        if (diff is not null) sb.Append(DiffSection(diff, features, titles));

        foreach (var group in features.GroupBy(f => f.RelativeDir))
        {
            if (group.Key.Length > 0) sb.Append($"<h2>{Esc(group.Key)}</h2>");
            sb.Append("<table><tbody><tr><th>Feature</th><th>Scenario</th><th>Result</th></tr>");
            foreach (var f in group)
            {
                for (var i = 0; i < f.Scenarios.Count; i++)
                {
                    var s = f.Scenarios[i];
                    sb.Append("<tr>");
                    if (i == 0)
                    {
                        sb.Append($"<td rowspan=\"{f.Scenarios.Count}\">{PageLink(titles.Of(f), FeatureLabel(f))}");
                        if (ReportsLink(o, f, "traces") is { } traces) sb.Append("<br/>").Append(traces);
                        sb.Append("</td>");
                    }
                    sb.Append($"<td>{PageLink(titles.Of(f), LivingDocEmitter.Humanize(s.MethodName), s.MethodName)}</td>");
                    sb.Append($"<td>{ResultLozenge(byId[$"{f.PagePath}/{s.MethodName}"])}</td></tr>");
                }
            }
            sb.Append("</tbody></table>");
        }
        return sb.ToString();
    }

    private static string DiffSection(SuiteDiff diff, IReadOnlyList<Feature> features, Titles titles)
    {
        var sb = new StringBuilder("<h2>Changes since baseline</h2>");
        if (diff.Changes.Count == 0)
            return sb.Append("<p><em>No changes to scenarios, steps or results.</em></p>").ToString();

        var known = features.Select(f => f.PagePath).ToHashSet();
        sb.Append("<ul>");
        foreach (var c in diff.Changes)
        {
            var (label, colour) = c.Kind switch
            {
                SuiteDiff.Added => ("added", "Green"),
                SuiteDiff.Removed => ("removed", "Grey"),
                SuiteDiff.StepsChanged => ("steps changed", "Yellow"),
                _ => (c.To == Status.Fail ? "now failing" : c.To == Status.Pass ? "now passing" : "result changed",
                      c.To == Status.Fail ? "Red" : "Green"),
            };
            var slash = c.Id.LastIndexOf('/');
            var title = slash > 0 && c.Kind != SuiteDiff.Removed && known.Contains(c.Id[..slash])
                ? PageLink(titles.Of(c.Id[..slash]), c.Title, c.Id[(slash + 1)..])
                : Esc(c.Title);
            sb.Append($"<li>{Lozenge(colour, label.ToUpperInvariant())} {title} <code>{Esc(c.Id)}</code></li>");
        }
        return sb.Append("</ul>").ToString();
    }

    /// <summary>
    /// Page → Feature → Given → When → Then, as one section per page object. Mermaid isn't available in
    /// Confluence, so each scenario is a table row; the feature cell links straight to the scenario.
    /// </summary>
    private static string Graph(
        IReadOnlyList<Feature> features, IReadOnlyDictionary<string, ScenarioManifest> byId, Titles titles, ConfluenceOptions o)
    {
        var sb = new StringBuilder();
        sb.Append(Nav(titles, o, null));
        sb.Append(GeneratedNotice());
        sb.Append("<p>Page → Feature → Given → When → Then. Shared pages and givens show reuse across scenarios.</p>");

        var rows = features
            .SelectMany(f => f.Scenarios.Select(s => (Feature: f, Scenario: s, Page: GraphEmitter.ResolvePage(s))))
            .GroupBy(r => r.Page);
        foreach (var page in rows)
        {
            sb.Append($"<h2>{Esc(page.Key)}</h2>");
            sb.Append("<table><tbody><tr><th>Feature</th><th>Given</th><th>When</th><th>Then</th><th>Result</th></tr>");
            foreach (var (f, s, _) in page)
            {
                string Cell(Func<ScenarioStep, bool> pick) =>
                    string.Join("<br/>", s.Steps.Where(pick).Select(x => StepLabel(x.Label)));
                sb.Append($"<tr><td>{PageLink(titles.Of(f), FeatureLabel(f) + " / " + LivingDocEmitter.Humanize(s.MethodName), s.MethodName)}</td>" +
                          $"<td>{Cell(x => x.Type == "Given")}</td><td>{Cell(x => x.Type == "When")}</td>" +
                          $"<td>{Cell(x => x.Type is "Then" or "And")}</td>" +
                          $"<td>{ResultLozenge(byId[$"{f.PagePath}/{s.MethodName}"])}</td></tr>");
            }
            sb.Append("</tbody></table>");
        }
        return sb.ToString();
    }

    // ---- building blocks ----

    private static string Nav(Titles titles, ConfluenceOptions o, string? extra)
    {
        var items = new List<string>
        {
            PageLink(titles.Root, "Features"),
            PageLink(titles.Graph, "Page graph"),
        };
        if (o.ReportsUrl is not null) items.Add(ExternalLink(o.ReportsUrl.TrimEnd('/') + "/", "Run reports"));
        if (extra is not null) items.Add(extra);
        return $"<p>{string.Join(" · ", items)}</p>";
    }

    private static string GeneratedNotice() =>
        "<ac:structured-macro ac:name=\"note\"><ac:rich-text-body><p>Generated by <strong>simplebehav-gen</strong> from test source. " +
        "Edits to this page are overwritten on the next publish.</p></ac:rich-text-body></ac:structured-macro>";

    private static string? ReportsLink(ConfluenceOptions o, Feature f, string text) =>
        o.ReportsUrl is null ? null : ExternalLink($"{o.ReportsUrl.TrimEnd('/')}/{f.PagePath}/", text);

    private static string ResultLozenge(ScenarioManifest s) => s.Status switch
    {
        Status.Pass => Lozenge("Green", "PASS") + $" <small>{s.DurationMs}ms</small>",
        Status.Fail => Lozenge("Red", "FAIL") + $" <small>{s.DurationMs}ms</small>",
        _ => Lozenge("Grey", "NOT RUN"),
    };

    private static string Lozenge(string colour, string title) =>
        Macro("status", ("colour", colour), ("title", title));

    private static string Macro(string name, params (string Key, string Value)[] parameters) =>
        $"<ac:structured-macro ac:name=\"{name}\">" +
        string.Concat(parameters.Select(p => $"<ac:parameter ac:name=\"{p.Key}\">{Esc(p.Value)}</ac:parameter>")) +
        "</ac:structured-macro>";

    private static string Anchor(string name) =>
        $"<ac:structured-macro ac:name=\"anchor\"><ac:parameter ac:name=\"\">{Esc(name)}</ac:parameter></ac:structured-macro>";

    private static string Code(string text) =>
        $"<ac:structured-macro ac:name=\"code\"><ac:plain-text-body>{Cdata(text)}</ac:plain-text-body></ac:structured-macro>";

    /// <summary>Link to another page in the same space by title, optionally to an anchor on it.</summary>
    private static string PageLink(string title, string text, string? anchor = null) =>
        $"<ac:link{(anchor is null ? "" : $" ac:anchor=\"{Esc(anchor)}\"")}><ri:page ri:content-title=\"{Esc(title)}\"/>" +
        $"<ac:plain-text-link-body>{Cdata(text)}</ac:plain-text-link-body></ac:link>";

    private static string ExternalLink(string url, string text) => $"<a href=\"{Esc(url)}\">{Esc(text)}</a>";

    // Quoted string literals read as prose; anything else (e.g. Pages.Checkout) is a code reference.
    private static string StepLabel(string label) =>
        label.Contains('.') && !label.Contains(' ') ? $"<code>{Esc(label)}</code>" : Esc(label);

    private static string Cdata(string s) => "<![CDATA[" + s.Replace("]]>", "]]]]><![CDATA[>") + "]]>";

    private static string Esc(string s) => WebUtility.HtmlEncode(s);
}
