using System.Net;
using System.Text;

namespace SimpleBehav.Reporter;

/// <summary>One scenario inside a suite report, paired with its artifact directory name.</summary>
public sealed record SuiteScenario(string Slug, ArtifactBundle Bundle);

/// <summary>
/// Renders the suite report — one page per test file, listing every scenario in it
/// next to the Playwright trace of the selected scenario.
/// </summary>
public static class DocsGenerator
{
    private static string StepTone(ArtifactStep step) => step.Error is not null ? "danger" :
        step.Type switch { StepType.Assert => "success", _ => "info" };

    private static string StepIcon(ArtifactStep step) => step.Error is not null ? "&#x2717;" :
        step.Type switch { StepType.Setup => "&#x2699;", StepType.Act => "&#x25B6;", StepType.Assert => "&#x2713;", _ => "&middot;" };

    private static string StepKeyword(ArtifactStep step) => step.Type switch
    {
        StepType.Setup => "Given",
        StepType.Act => "When",
        StepType.Assert => "Then",
        _ => "Step",
    };

    private static string Esc(string s) => WebUtility.HtmlEncode(s);

    private static string StepTimeline(IEnumerable<ArtifactStep> steps)
    {
        var sb = new StringBuilder();
        foreach (var s in steps)
        {
            sb.AppendLine($"          <a class=\"timeline-event {StepTone(s)}\" href=\"#playwright-trace\">");
            sb.AppendLine($"            <div class=\"timeline-time\">{s.DurationMs}ms</div>");
            sb.AppendLine($"            <div class=\"timeline-title\">{StepIcon(s)} {StepKeyword(s)} {Esc(s.Label)}</div>");
            if (s.Error is not null)
                sb.AppendLine($"            <div class=\"timeline-body\"><pre class=\"pre-error\">{Esc(s.Error)}</pre></div>");
            sb.AppendLine("          </a>");
        }
        return sb.ToString();
    }

    private static string ScenarioList(IReadOnlyList<SuiteScenario> scenarios, int selectedIndex)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < scenarios.Count; i++)
        {
            var (slug, bundle) = (scenarios[i].Slug, scenarios[i].Bundle);
            var tone = bundle.Status == "pass" ? "success" : "danger";
            var label = bundle.Status == "pass" ? "PASS" : "FAIL";
            var open = i == selectedIndex ? " open" : "";
            var stepCount = bundle.Steps.Count;

            sb.AppendLine($"        <details class=\"scenario\" data-slug=\"{Esc(slug)}\" data-title=\"{Esc(bundle.Title)}\"{open}>");
            sb.AppendLine("          <summary>");
            sb.AppendLine($"            <span class=\"scenario-name\">{Esc(bundle.Title)}</span>");
            sb.AppendLine($"            <span class=\"pill {tone}\">{label}</span>");
            sb.AppendLine($"            <span class=\"scenario-meta\">{bundle.DurationMs}ms &middot; {stepCount} step{(stepCount != 1 ? "s" : "")}</span>");
            sb.AppendLine("          </summary>");
            sb.AppendLine("          <div class=\"timeline\">");
            sb.Append(StepTimeline(bundle.Steps));
            sb.AppendLine("          </div>");
            sb.AppendLine("        </details>");
        }
        return sb.ToString();
    }

    /// <param name="suiteTitle">Test file the scenarios came from.</param>
    /// <param name="scenarios">Scenarios in the suite, in execution order.</param>
    /// <param name="traceViewerBase">Relative path from this report to trace-viewer/index.html.</param>
    public static string Generate(string suiteTitle, IReadOnlyList<SuiteScenario> scenarios, string traceViewerBase)
    {
        var total = scenarios.Count;
        var failed = scenarios.Count(s => s.Bundle.Status != "pass");
        var passed = total - failed;
        var durationMs = scenarios.Sum(s => s.Bundle.DurationMs);
        var statusTone = failed == 0 ? "success" : "danger";
        var statusLabel = failed == 0 ? "ALL PASSED" : $"{failed} FAILED";
        var selectedIndex = scenarios.ToList().FindIndex(s => s.Bundle.Status != "pass");
        if (selectedIndex < 0) selectedIndex = 0;
        var generatedAt = DateTime.UtcNow.ToString("o");

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\">");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta charset=\"UTF-8\">");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
        sb.AppendLine($"<title>{Esc(suiteTitle)}</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("""
  :root {
    --bg: #ffffff; --surface: #ffffff; --surface-alt: #f6f8fa;
    --border: #d0d7de; --border-strong: #afb8c1;
    --text: #1f2328; --text-muted: #59636e; --text-faint: #818b98;
    --accent: #0969da;
    --success: #1a7f37; --success-bg: #dafbe1;
    --warning: #9a6700; --warning-bg: #fff8c5;
    --danger: #cf222e; --danger-bg: #ffebe9;
    --info: #0969da; --info-bg: #ddf4ff;
    --radius: 6px;
  }
  *, *::before, *::after { box-sizing: border-box; margin: 0; padding: 0; }
  html { scroll-behavior: smooth; }
  body { font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Helvetica, Arial, sans-serif;
         font-size: 14px; line-height: 1.55; color: var(--text); background: var(--bg); }
  a { color: var(--accent); }
  .container { width: 100%; max-width: 1920px; margin: 0 auto; padding: 20px clamp(12px, 2vw, 32px); }
  header { padding-bottom: 16px; border-bottom: 1px solid var(--border); margin-bottom: 20px; }
  header h1 { font-size: 20px; font-weight: 600; }
  .subtitle { color: var(--text-muted); margin-top: 4px; font-size: 13px; }
  h2 { font-size: 15px; font-weight: 600; padding-bottom: 8px; border-bottom: 1px solid var(--border); margin-bottom: 14px; }
  .stat-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(150px, 1fr)); gap: 12px; margin-bottom: 20px; }
  .stat { padding: 14px; border: 1px solid var(--border); border-radius: var(--radius); background: var(--surface-alt); }
  .stat.tone-success { border-color: var(--success); background: var(--success-bg); }
  .stat.tone-danger  { border-color: var(--danger);  background: var(--danger-bg); }
  .stat.tone-info    { border-color: var(--info);    background: var(--info-bg); }
  .stat-value { font-size: 22px; font-weight: 700; line-height: 1.2; }
  .stat-label { font-size: 12px; color: var(--text-muted); margin-top: 2px; }
  .pill { display: inline-block; padding: 1px 8px; border-radius: 20px; font-size: 11px; font-weight: 600; }
  .pill.success { background: var(--success-bg); color: var(--success); }
  .pill.danger  { background: var(--danger-bg);  color: var(--danger); }
  .pill.info    { background: var(--info-bg);    color: var(--info); }
  .grid-2 { display: grid; grid-template-columns: minmax(300px, 24%) minmax(0, 1fr); gap: clamp(12px, 1.5vw, 24px);
            align-items: start; }
  .panel { min-width: 0; }
  .scenario-list { display: flex; flex-direction: column; gap: 10px;
                   max-height: calc(100vh - 210px); overflow-y: auto; padding-right: 4px; }
  .scenario { border: 1px solid var(--border); border-radius: var(--radius); background: var(--surface); }
  .scenario.selected { border-color: var(--accent); box-shadow: 0 0 0 1px var(--accent); }
  .scenario > summary { cursor: pointer; padding: 10px 12px; display: flex; flex-wrap: wrap; gap: 6px;
                        align-items: center; background: var(--surface-alt); border-radius: var(--radius); }
  .scenario[open] > summary { border-bottom: 1px solid var(--border); border-radius: var(--radius) var(--radius) 0 0; }
  .scenario-name { font-weight: 600; font-size: 13px; word-break: break-word; }
  .scenario-meta { font-size: 11px; color: var(--text-faint); font-family: monospace; }
  .timeline { display: flex; flex-direction: column; gap: 8px; padding: 12px; }
  .timeline-event { display: block; padding: 10px 12px; border: 1px solid var(--border); border-radius: var(--radius);
                    border-left: 4px solid var(--border-strong); background: var(--surface-alt);
                    color: var(--text); text-decoration: none; }
  .timeline-event:hover { border-color: var(--accent); }
  .timeline-event.success { border-left-color: var(--success); background: var(--success-bg); }
  .timeline-event.danger  { border-left-color: var(--danger);  background: var(--danger-bg); }
  .timeline-event.info    { border-left-color: var(--info);    background: var(--info-bg); }
  .timeline-time  { font-size: 11px; color: var(--text-faint); font-family: monospace; }
  .timeline-title { font-weight: 600; font-size: 13px; }
  .timeline-body  { margin-top: 8px; }
  .trace-panel { position: sticky; top: 16px; }
  .trace-meta { font-size: 12px; color: var(--text-muted); margin-bottom: 8px; }
  iframe { width: 100%; height: clamp(460px, calc(100vh - 250px), 1200px); border: 1px solid var(--border);
           border-radius: var(--radius); background: var(--surface-alt); }
  .trace-links { display: flex; flex-wrap: wrap; gap: 12px; margin-top: 8px; font-size: 12px; }
  pre { font-family: "SFMono-Regular", Consolas, monospace; font-size: 12px; background: var(--surface-alt);
        border: 1px solid var(--border); border-radius: var(--radius); padding: 10px; overflow-x: auto; white-space: pre-wrap; }
  pre.pre-error { background: var(--danger-bg); border-color: var(--danger); }
  footer { margin-top: 24px; padding-top: 12px; border-top: 1px solid var(--border);
           font-size: 12px; color: var(--text-faint); }
  @media (max-width: 1100px) {
    .grid-2 { grid-template-columns: 1fr; }
    .scenario-list { max-height: none; }
    .trace-panel { position: static; }
    iframe { height: 75vh; }
  }
""");
        sb.AppendLine("</style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine("<div class=\"container\">");
        sb.AppendLine("  <header>");
        sb.AppendLine($"    <h1>{Esc(suiteTitle)}</h1>");
        sb.AppendLine("    <p class=\"subtitle\">");
        sb.AppendLine($"      <span class=\"pill {statusTone}\">{statusLabel}</span>");
        sb.AppendLine($"      &nbsp;&middot;&nbsp;{total} scenario{(total != 1 ? "s" : "")}");
        sb.AppendLine($"      &nbsp;&middot;&nbsp;{durationMs}ms total");
        sb.AppendLine("    </p>");
        sb.AppendLine("  </header>");
        sb.AppendLine("  <section class=\"stat-grid\">");
        sb.AppendLine($"    <div class=\"stat tone-{statusTone}\">");
        sb.AppendLine($"      <div class=\"stat-value\">{statusLabel}</div>");
        sb.AppendLine("      <div class=\"stat-label\">Result</div>");
        sb.AppendLine("    </div>");
        sb.AppendLine("    <div class=\"stat tone-info\">");
        sb.AppendLine($"      <div class=\"stat-value\">{total}</div>");
        sb.AppendLine("      <div class=\"stat-label\">Scenarios</div>");
        sb.AppendLine("    </div>");
        sb.AppendLine("    <div class=\"stat\">");
        sb.AppendLine($"      <div class=\"stat-value\">{passed}</div>");
        sb.AppendLine("      <div class=\"stat-label\">Passed</div>");
        sb.AppendLine("    </div>");
        sb.AppendLine($"    <div class=\"stat tone-{(failed > 0 ? "danger" : "success")}\">");
        sb.AppendLine($"      <div class=\"stat-value\">{failed}</div>");
        sb.AppendLine("      <div class=\"stat-label\">Failed</div>");
        sb.AppendLine("    </div>");
        sb.AppendLine("  </section>");
        sb.AppendLine("  <div class=\"grid-2\">");
        sb.AppendLine("    <section class=\"panel\">");
        sb.AppendLine("      <h2>Test Scenario</h2>");
        sb.AppendLine("      <div class=\"scenario-list\">");
        sb.Append(ScenarioList(scenarios, selectedIndex));
        sb.AppendLine("      </div>");
        sb.AppendLine("    </section>");
        sb.AppendLine("    <section class=\"panel trace-panel\" id=\"playwright-trace\">");
        sb.AppendLine("      <h2>Playwright trace</h2>");
        sb.AppendLine("      <p class=\"trace-meta\">Showing <strong id=\"trace-scenario\">&mdash;</strong></p>");
        sb.AppendLine("      <iframe id=\"trace-viewer\" title=\"Playwright Trace Viewer\"></iframe>");
        sb.AppendLine("      <div class=\"trace-links\">");
        sb.AppendLine("        <a id=\"open-trace\" href=\"#playwright-trace\">Open trace in a new tab</a>");
        sb.AppendLine("        <a id=\"download-trace\" href=\"#playwright-trace\" download>Download trace.zip</a>");
        sb.AppendLine("        <a id=\"download-cucumber\" href=\"#playwright-trace\" download>Download cucumber.json</a>");
        sb.AppendLine("      </div>");
        sb.AppendLine("    </section>");
        sb.AppendLine("  </div>");
        sb.AppendLine($"  <footer>Generated by <strong>simplebehav</strong> &middot; {generatedAt}</footer>");
        sb.AppendLine("</div>");
        sb.AppendLine("<script>");
        sb.AppendLine($"  const viewerBase = {System.Text.Json.JsonSerializer.Serialize(traceViewerBase)};");
        sb.AppendLine("""
  const frame = document.getElementById('trace-viewer');
  const label = document.getElementById('trace-scenario');
  const openLink = document.getElementById('open-trace');
  const traceLink = document.getElementById('download-trace');
  const cucumberLink = document.getElementById('download-cucumber');
  const scenarios = [...document.querySelectorAll('.scenario')];

  function select(scenario) {
    const slug = scenario.dataset.slug;
    const trace = new URL(slug + '/trace.zip', window.location.href);
    const viewer = new URL(viewerBase, window.location.href);
    viewer.searchParams.set('trace', trace.href);
    frame.src = viewer.href;
    openLink.href = viewer.href;
    traceLink.href = trace.href;
    cucumberLink.href = new URL(slug + '/cucumber.json', window.location.href).href;
    label.textContent = scenario.dataset.title;
    scenarios.forEach(s => s.classList.toggle('selected', s === scenario));
  }

  scenarios.forEach(scenario => scenario.addEventListener('click', () => select(scenario)));
  const initial = scenarios.find(s => s.hasAttribute('open')) ?? scenarios[0];
  if (initial) select(initial);
""");
        sb.AppendLine("</script>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        return sb.ToString();
    }
}
