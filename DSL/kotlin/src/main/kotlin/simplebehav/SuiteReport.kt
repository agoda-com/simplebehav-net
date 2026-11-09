package simplebehav

import java.nio.file.Files
import java.nio.file.Path
import kotlin.io.path.readText
import kotlin.io.path.writeText
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json

internal data class SuiteScenario(val slug: String, val bundle: ArtifactBundle)

/**
 * Writes the suite page for one test file. Called after every scenario so the
 * report is complete once the last scenario in the file finishes.
 */
internal object SuiteReporter {
    private val json = Json { ignoreUnknownKeys = true; explicitNulls = false }

    fun write(suiteDir: Path, suiteTitle: String, traceViewerBase: String) {
        if (!Files.isDirectory(suiteDir)) return
        val scenarios = mutableListOf<SuiteScenario>()
        Files.newDirectoryStream(suiteDir).use { entries ->
            for (entry in entries) {
                if (!Files.isDirectory(entry)) continue
                val bundlePath = entry.resolve("bundle.json")
                if (!Files.isRegularFile(bundlePath)) continue
                val bundle = json.decodeFromString<ArtifactBundle>(bundlePath.readText())
                scenarios += SuiteScenario(entry.fileName.toString(), bundle)
            }
        }
        scenarios.sortBy { it.bundle.startedAt }
        suiteDir.resolve("index.html").writeText(
            DocsGenerator.generate(suiteTitle, scenarios, traceViewerBase),
        )
    }
}

/**
 * Renders the suite report — one page per test file, listing every scenario in it
 * next to the Playwright trace of the selected scenario.
 */
internal object DocsGenerator {
    fun generate(suiteTitle: String, scenarios: List<SuiteScenario>, traceViewerBase: String): String {
        val total = scenarios.size
        val failed = scenarios.count { it.bundle.status != "pass" }
        val passed = total - failed
        val durationMs = scenarios.sumOf { it.bundle.durationMs }
        val statusTone = if (failed == 0) "success" else "danger"
        val statusLabel = if (failed == 0) "ALL PASSED" else "$failed FAILED"
        val selectedIndex = scenarios.indexOfFirst { it.bundle.status != "pass" }.let { if (it < 0) 0 else it }
        val generatedAt = java.time.Instant.now().toString()

        return buildString {
            appendLine("<!DOCTYPE html>")
            appendLine("<html lang=\"en\">")
            appendLine("<head>")
            appendLine("<meta charset=\"UTF-8\">")
            appendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">")
            appendLine("<title>${esc(suiteTitle)}</title>")
            appendLine("<style>")
            appendLine(STYLE)
            appendLine("</style>")
            appendLine("</head>")
            appendLine("<body>")
            appendLine("<div class=\"container\">")
            appendLine("  <header>")
            appendLine("    <h1>${esc(suiteTitle)}</h1>")
            appendLine("    <p class=\"subtitle\">")
            appendLine("      <span class=\"pill $statusTone\">$statusLabel</span>")
            appendLine("      &nbsp;&middot;&nbsp;$total scenario${if (total != 1) "s" else ""}")
            appendLine("      &nbsp;&middot;&nbsp;${durationMs}ms total")
            appendLine("    </p>")
            appendLine("  </header>")
            appendLine("  <section class=\"stat-grid\">")
            appendLine("    <div class=\"stat tone-$statusTone\">")
            appendLine("      <div class=\"stat-value\">$statusLabel</div>")
            appendLine("      <div class=\"stat-label\">Result</div>")
            appendLine("    </div>")
            appendLine("    <div class=\"stat tone-info\">")
            appendLine("      <div class=\"stat-value\">$total</div>")
            appendLine("      <div class=\"stat-label\">Scenarios</div>")
            appendLine("    </div>")
            appendLine("    <div class=\"stat\">")
            appendLine("      <div class=\"stat-value\">$passed</div>")
            appendLine("      <div class=\"stat-label\">Passed</div>")
            appendLine("    </div>")
            appendLine("    <div class=\"stat tone-${if (failed > 0) "danger" else "success"}\">")
            appendLine("      <div class=\"stat-value\">$failed</div>")
            appendLine("      <div class=\"stat-label\">Failed</div>")
            appendLine("    </div>")
            appendLine("  </section>")
            appendLine("  <div class=\"grid-2\">")
            appendLine("    <section class=\"panel\">")
            appendLine("      <h2>Test Scenario</h2>")
            appendLine("      <div class=\"scenario-list\">")
            append(scenarioList(scenarios, selectedIndex))
            appendLine("      </div>")
            appendLine("    </section>")
            appendLine("    <section class=\"panel trace-panel\" id=\"playwright-trace\">")
            appendLine("      <h2>Playwright trace</h2>")
            appendLine("      <p class=\"trace-meta\">Showing <strong id=\"trace-scenario\">&mdash;</strong></p>")
            appendLine("      <iframe id=\"trace-viewer\" title=\"Playwright Trace Viewer\"></iframe>")
            appendLine("      <div class=\"trace-links\">")
            appendLine("        <a id=\"open-trace\" href=\"#playwright-trace\">Open trace in a new tab</a>")
            appendLine("        <a id=\"download-trace\" href=\"#playwright-trace\" download>Download trace.zip</a>")
            appendLine("        <a id=\"download-cucumber\" href=\"#playwright-trace\" download>Download cucumber.json</a>")
            appendLine("      </div>")
            appendLine("    </section>")
            appendLine("  </div>")
            appendLine("  <footer>Generated by <strong>simplebehav</strong> &middot; $generatedAt</footer>")
            appendLine("</div>")
            appendLine("<script>")
            appendLine("  const viewerBase = ${Json.encodeToString(traceViewerBase)};")
            appendLine(SCRIPT)
            appendLine("</script>")
            appendLine("</body>")
            appendLine("</html>")
        }
    }

    private fun scenarioList(scenarios: List<SuiteScenario>, selectedIndex: Int): String = buildString {
        scenarios.forEachIndexed { index, scenario ->
            val bundle = scenario.bundle
            val tone = if (bundle.status == "pass") "success" else "danger"
            val label = if (bundle.status == "pass") "PASS" else "FAIL"
            val open = if (index == selectedIndex) " open" else ""
            val stepCount = bundle.steps.size
            appendLine("        <details class=\"scenario\" data-slug=\"${esc(scenario.slug)}\" data-title=\"${esc(bundle.title)}\"$open>")
            appendLine("          <summary>")
            appendLine("            <span class=\"scenario-name\">${esc(bundle.title)}</span>")
            appendLine("            <span class=\"pill $tone\">$label</span>")
            appendLine("            <span class=\"scenario-meta\">${bundle.durationMs}ms &middot; $stepCount step${if (stepCount != 1) "s" else ""}</span>")
            appendLine("          </summary>")
            appendLine("          <div class=\"timeline\">")
            append(stepTimeline(bundle.steps))
            appendLine("          </div>")
            appendLine("        </details>")
        }
    }

    private fun stepTimeline(steps: List<ArtifactStep>): String = buildString {
        for (step in steps) {
            appendLine("          <a class=\"timeline-event ${stepTone(step)}\" href=\"#playwright-trace\">")
            appendLine("            <div class=\"timeline-time\">${step.durationMs}ms</div>")
            appendLine("            <div class=\"timeline-title\">${stepIcon(step)} ${stepKeyword(step)} ${esc(step.label)}</div>")
            if (step.error != null) {
                appendLine("            <div class=\"timeline-body\"><pre class=\"pre-error\">${esc(step.error)}</pre></div>")
            }
            appendLine("          </a>")
        }
    }

    private fun stepTone(step: ArtifactStep): String = when {
        step.error != null -> "danger"
        step.type == StepType.Assert -> "success"
        else -> "info"
    }

    private fun stepIcon(step: ArtifactStep): String = when {
        step.error != null -> "&#x2717;"
        step.type == StepType.Setup -> "&#x2699;"
        step.type == StepType.Act -> "&#x25B6;"
        step.type == StepType.Assert -> "&#x2713;"
        else -> "&middot;"
    }

    private fun stepKeyword(step: ArtifactStep): String = when (step.type) {
        StepType.Setup -> "Given"
        StepType.Act -> "When"
        StepType.Assert -> "Then"
    }

    private fun esc(value: String): String = buildString(value.length) {
        for (ch in value) {
            when (ch) {
                '&' -> append("&amp;")
                '<' -> append("&lt;")
                '>' -> append("&gt;")
                '"' -> append("&quot;")
                else -> append(ch)
            }
        }
    }
}

private const val STYLE = """
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
"""

private const val SCRIPT = """
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
"""
