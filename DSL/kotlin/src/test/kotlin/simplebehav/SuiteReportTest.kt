package simplebehav

import kotlin.test.Test
import kotlin.test.assertTrue

class SuiteReportTest {
    @Test
    fun listsEveryScenarioInTheSuite() {
        val html = DocsGenerator.generate(
            "CheckoutTests",
            listOf(
                scenario(
                    "user places order",
                    "pass",
                    ArtifactStep(StepType.Setup, "checkout page is available", 4),
                    ArtifactStep(StepType.Assert, "toast visible", 300),
                ),
                scenario(
                    "user clears card",
                    "pass",
                    ArtifactStep(StepType.Assert, "submit stays disabled", 40),
                ),
            ),
            "../../trace-viewer/index.html",
        )

        assertTrue(html.contains("CheckoutTests"))
        assertTrue(html.contains("user places order"))
        assertTrue(html.contains("user clears card"))
        assertTrue(html.contains("Given checkout page is available"))
        assertTrue(html.contains("Then toast visible"))
        assertTrue(html.contains("ALL PASSED"))
    }

    @Test
    fun pointsTraceViewerAtSuppliedBase() {
        val html = DocsGenerator.generate(
            "CheckoutTests",
            listOf(scenario("user places order", "pass", ArtifactStep(StepType.Assert, "toast visible", 10))),
            "../../trace-viewer/index.html",
        )

        assertTrue(html.contains("\"../../trace-viewer/index.html\""))
        assertTrue(html.contains("href=\"#playwright-trace\""))
        assertTrue(!html.contains("data:image/png;base64"))
    }

    @Test
    fun failingScenarioIsSelectedAndTonedDanger() {
        val html = DocsGenerator.generate(
            "CheckoutTests",
            listOf(
                scenario("user places order", "pass", ArtifactStep(StepType.Assert, "toast visible", 10)),
                scenario(
                    "user clears card",
                    "fail",
                    ArtifactStep(StepType.Assert, "FAILED: timeout", 500, "timeout"),
                ),
            ),
            "../trace-viewer/index.html",
        )

        assertTrue(html.contains("1 FAILED"))
        assertTrue(html.contains("pre-error"))
        assertTrue(html.contains("data-title=\"user clears card\" open"))
    }

    private fun scenario(title: String, status: String, vararg steps: ArtifactStep) = SuiteScenario(
        title.replace(' ', '-'),
        ArtifactBundle(
            id = "id",
            title = title,
            status = status,
            startedAt = "2026-11-09T00:00:00Z",
            durationMs = 1234,
            steps = steps.toList(),
        ),
    )
}
