using System.Text;

namespace SimpleBehav.SpecGen;

/// <summary>
/// Emits a separate left-to-right page graph beside living documentation:
/// Page → Feature → Given → When → Then. Existing living-doc pages are unchanged.
/// </summary>
public static class GraphEmitter
{
    public const string FileName = "graph.html";

    /// <param name="reportsBase">
    /// Relative path from the site root to the test run reports. Feature nodes link to the run report &amp; traces
    /// there; when null they link to the feature's living-doc page instead.
    /// </param>
    public static string Emit(IReadOnlyList<Feature> features, string? reportsBase = null) =>
        Page(Mermaid(features, reportsBase),
            LivingDocEmitter.TopBar(new LivingDocEmitter.Nav("", reportsBase is null ? null : $"{reportsBase.Trim('/')}/"),
                LivingDocEmitter.NavSection.Graph));

    /// <summary>
    /// Prefer <c>When&lt;T&gt;</c> when T is a real page object; otherwise fall back to the Given setup
    /// (e.g. <c>Pages.Checkout</c>) so <c>When&lt;IPage&gt;</c> scenarios still group by page.
    /// </summary>
    public static string ResolvePage(Scenario scenario)
    {
        var when = scenario.Steps.FirstOrDefault(s => s.Type == "When");
        if (when?.TypeArg is { Length: > 0 } t && t is not ("IPage" or "object" or "Page"))
            return t;

        var given = scenario.Steps.FirstOrDefault(s => s.Type == "Given");
        return given?.Label is { Length: > 0 } g ? g : "Unknown";
    }

    private static string Mermaid(IReadOnlyList<Feature> features, string? reportsBase)
    {
        var sb = new StringBuilder();
        sb.AppendLine("flowchart LR");

        var edges = new HashSet<string>(StringComparer.Ordinal);
        var declared = new HashSet<string>(StringComparer.Ordinal);
        var seq = 0;

        void Declare(string id, string label, string cssClass)
        {
            if (!declared.Add(id)) return;
            sb.AppendLine($"  {id}[\"{EscMermaid(label)}\"]:::{cssClass}");
        }

        // The graph sits at the site root, so hrefs are relative to it.
        void LinkFeature(string id, Feature feature)
        {
            var href = reportsBase is null
                ? $"{feature.PagePath}/"
                : $"{reportsBase.Trim('/')}/{feature.PagePath}/";
            var tip = reportsBase is null ? "Open feature" : "Open run report & traces";
            sb.AppendLine($"  click {id} href \"{EscMermaid(href)}\" \"{EscMermaid(tip)}\" _self");
        }

        void Edge(string from, string to)
        {
            var key = $"{from}->{to}";
            if (!edges.Add(key)) return;
            sb.AppendLine($"  {from} --> {to}");
        }

        foreach (var feature in features)
        {
            var featureId = Id("F", feature.PagePath);
            var featureLabel = LivingDocEmitter.Humanize(feature.Name.Replace("Tests", ""));

            foreach (var scenario in feature.Scenarios)
            {
                var page = ResolvePage(scenario);
                var pageId = Id("P", page);
                Declare(pageId, page, "page");
                Declare(featureId, featureLabel, "feature");
                Edge(pageId, featureId);

                var givens = scenario.Steps.Where(s => s.Type == "Given").ToList();
                var whens = scenario.Steps.Where(s => s.Type == "When").ToList();
                var thens = scenario.Steps.Where(s => s.Type is "Then" or "And").ToList();

                // Scenario-unique When/Then ids so shared Givens fan out cleanly.
                var pathKey = $"{feature.PagePath}/{scenario.MethodName}";

                if (givens.Count == 0)
                {
                    var orphanWhen = whens.FirstOrDefault();
                    if (orphanWhen is null) continue;
                    var whenId = Id("W", $"{pathKey}/{orphanWhen.Label}/{++seq}");
                    Declare(whenId, orphanWhen.Label, "when");
                    Edge(featureId, whenId);
                    foreach (var then in thens)
                    {
                        var thenId = Id("T", $"{pathKey}/{then.Label}/{++seq}");
                        Declare(thenId, then.Label, "then");
                        Edge(whenId, thenId);
                    }
                    continue;
                }

                foreach (var given in givens)
                {
                    var givenId = Id("G", given.Label);
                    Declare(givenId, given.Label, "given");
                    Edge(featureId, givenId);

                    foreach (var when in whens)
                    {
                        var whenId = Id("W", $"{pathKey}/{when.Label}/{++seq}");
                        Declare(whenId, when.Label, "when");
                        Edge(givenId, whenId);

                        foreach (var then in thens)
                        {
                            var thenId = Id("T", $"{pathKey}/{then.Label}/{++seq}");
                            Declare(thenId, then.Label, "then");
                            Edge(whenId, thenId);
                        }
                    }
                }
            }
        }

        foreach (var feature in features)
        {
            var featureId = Id("F", feature.PagePath);
            if (declared.Contains(featureId)) LinkFeature(featureId, feature);
        }

        sb.AppendLine("  classDef page fill:#ddf4ff,stroke:#0969da,color:#0969da,font-weight:600");
        sb.AppendLine("  classDef feature fill:#f6f8fa,stroke:#59636e,color:#1f2328");
        sb.AppendLine("  classDef given fill:#fff8c5,stroke:#9a6700,color:#9a6700");
        sb.AppendLine("  classDef when fill:#ddf4ff,stroke:#0969da,color:#1f2328");
        sb.AppendLine("  classDef then fill:#dafbe1,stroke:#1a7f37,color:#1a7f37");
        return sb.ToString();
    }

    private static string Id(string prefix, string raw)
    {
        var cleaned = new string(raw.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
        if (cleaned.Length == 0 || char.IsDigit(cleaned[0])) cleaned = "n" + cleaned;
        return $"{prefix}_{cleaned}";
    }

    private static string EscMermaid(string s) =>
        s.Replace("\"", "#quot;").Replace("\n", " ").Replace("\r", "");

    private static string Page(string mermaid, string topBar) => $$"""
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<title>Page graph &mdash; SimpleBehav</title>
<style>
  :root { --border:#d0d7de; --alt:#f6f8fa; --text:#1f2328; --muted:#59636e; --accent:#0969da;
          --info:#0969da; --info-bg:#ddf4ff; --ok:#1a7f37; --ok-bg:#dafbe1; --warn:#9a6700; --warn-bg:#fff8c5; }
  *,*::before,*::after { box-sizing:border-box; }
  body { font-family:-apple-system,BlinkMacSystemFont,"Segoe UI",Helvetica,Arial,sans-serif; font-size:14px;
         line-height:1.55; color:var(--text); margin:0; background:#fff; }
  header:not(.topbar) { padding:20px 24px 12px; border-bottom:1px solid var(--border); }
  h1 { font-size:22px; margin:0 0 4px; }
  .subtitle { color:var(--muted); font-size:13px; margin:0 0 12px; }
  .legend { display:flex; flex-wrap:wrap; gap:8px; margin:0 0 4px; }
  .pill { font-size:11px; font-weight:600; padding:2px 10px; border-radius:20px; border:1px solid var(--border); }
  .pill.page { background:var(--info-bg); color:var(--info); border-color:#54aeff; }
  .pill.feature { background:var(--alt); color:var(--muted); }
  .mermaid .feature { cursor:pointer; }
  .pill.given { background:var(--warn-bg); color:var(--warn); border-color:#d4a72c; }
  .pill.when { background:var(--info-bg); color:var(--text); border-color:#54aeff; }
  .pill.then { background:var(--ok-bg); color:var(--ok); border-color:#4ac26b; }
  a { color:var(--accent); text-decoration:none; } a:hover { text-decoration:underline; }
  .graph { padding:16px 8px 32px; overflow-x:auto; }
  .mermaid { min-width:100%; }
  {{LivingDocEmitter.NavCss}}
  footer { padding:0 24px 24px; color:var(--muted); font-size:12px; }
</style>
</head>
<body>
{{topBar}}
<header>
  <h1>Page graph</h1>
  <p class="subtitle">Page → Feature → Given → When → Then &middot; left-to-right</p>
  <div class="legend">
    <span class="pill page">Page</span>
    <span class="pill feature">Feature (click for traces)</span>
    <span class="pill given">Given</span>
    <span class="pill when">When</span>
    <span class="pill then">Then</span>
  </div>
</header>
<div class="graph">
<pre class="mermaid">
{{mermaid.TrimEnd()}}
</pre>
</div>
<footer>Generated by <strong>simplebehav-gen</strong>. Shared Given nodes show reuse across scenarios.</footer>
<script type="module">
  import mermaid from 'https://cdn.jsdelivr.net/npm/mermaid@11/dist/mermaid.esm.min.mjs';
  // Defaults (50k chars / 500 edges) are too small for large suites before we shard by page.
  mermaid.initialize({
    startOnLoad: true,
    maxTextSize: 500000,
    maxEdges: 5000,
    flowchart: { htmlLabels: true, curve: 'basis' },
    theme: 'neutral'
  });
</script>
</body>
</html>
""";
}
