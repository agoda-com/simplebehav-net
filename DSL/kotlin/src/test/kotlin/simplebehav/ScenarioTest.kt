package simplebehav

import java.nio.file.Files
import java.nio.file.Path
import kotlin.reflect.KClass
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertSame
import kotlin.test.assertTrue
import kotlinx.coroutines.runBlocking
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive

class ScenarioTest {
    @Test
    fun assertionFailuresKeepTheirIdentityAndWriteReportsAndTraces() = runBlocking {
        for (phase in listOf("given", "when", "then")) {
            val out = Files.createTempDirectory("simplebehav-assertion-$phase")
            val original = assertFailsWith<AssertionError> { assertEquals("expected", "actual") }
            val adapter = FailureRecordingBuilder(if (phase == "given") original else null)
            val runner = ScenarioRunner(out.toString(), listOf("suite"), phase, adapter)
            runner.addSetup(setup("precondition") {})
            runner.setWhen("action") { if (phase == "when") throw original }
            val assertion = object : Assertion {
                override val label = "outcome"
                override suspend fun run(context: SetupContext) {
                    if (phase == "then") throw original
                }
            }

            val failure = assertFailsWith<AssertionError> {
                runner.execute(listOf("outcome" to assertion))
            }

            assertSame(original, failure)
            val scenarioDir = out.resolve("suite/$phase")
            assertTrue(Files.exists(scenarioDir.resolve("bundle.json")), "$phase bundle is missing")
            val bundle = Json.parseToJsonElement(Files.readString(scenarioDir.resolve("bundle.json")))
                .jsonObject
            assertEquals("fail", bundle.getValue("status").jsonPrimitive.content)
            assertTrue(Files.exists(scenarioDir.resolve("cucumber.json")))
            assertTrue(Files.readString(out.resolve("suite/index.html")).contains("1 FAILED"))
            assertTrue(Files.exists(scenarioDir.resolve("trace.zip")), "$phase trace is missing")
            assertTrue(adapter.viewerEnsured)
        }
    }

    @Test
    fun ordinaryExceptionsKeepScenarioContextAndTheOriginalCause() = runBlocking {
        val out = Files.createTempDirectory("simplebehav-exception")
        val original = IllegalArgumentException("invalid input")

        val failure = assertFailsWith<IllegalStateException> {
            failingRunner(out, FailureRecordingBuilder(), original).execute(emptyList())
        }

        assertTrue(failure.message!!.contains("scenario"))
        assertSame(original, failure.cause)
    }

    @Test
    fun jvmErrorsAreNotConvertedIntoScenarioFailures() = runBlocking {
        val out = Files.createTempDirectory("simplebehav-jvm-error")
        val original = LinkageError("broken linkage")

        val failure = assertFailsWith<LinkageError> {
            failingRunner(out, FailureRecordingBuilder(), original).execute(emptyList())
        }

        assertSame(original, failure)
        assertTrue(!Files.exists(out.resolve("suite/scenario/bundle.json")))
    }

    private fun failingRunner(
        out: Path,
        adapter: FailureRecordingBuilder,
        failure: Throwable,
    ) = ScenarioRunner(out.toString(), listOf("suite"), "scenario", adapter).apply {
        setWhen("action") { throw failure }
    }

    @Test
    fun scenarioRecordsGivenWhenThen() = runBlocking {
        val out = Files.createTempDirectory("simplebehav")
        val adapter = RecordingBuilder()
        Spec.configure(adapter, out.toString(), "sample")
        Spec.testName = "AddsItem"
        Spec.testClass = ScenarioTest::class
        Spec.configureScenario = null

        Spec.scenario()
            .given(setup("catalog is stocked") {})
            .When<String>("user adds the item") { adapter.seen = it }
            .then("cart shows the item", object : Assertion {
                override val label = "cart shows the item"
                override suspend fun run(context: SetupContext) {
                    assertEquals("widget", adapter.seen)
                }
            })
            .run()

        assertEquals(listOf("catalog is stocked"), adapter.applied)
        val html = readSuitePage(out)
        assertTrue(html.contains("AddsItem"))
        assertTrue(html.contains("ALL PASSED"))
        assertTrue(html.contains("Given catalog is stocked"))
        assertTrue(html.contains("When user adds the item"))
        assertTrue(html.contains("Then cart shows the item"))
    }

    @Test
    fun failedScenarioStillWritesTheSuitePage() = runBlocking {
        val out = Files.createTempDirectory("simplebehav")
        Spec.configure(RecordingBuilder(), out.toString(), "sample")
        Spec.testName = "RejectsItem"
        Spec.testClass = ScenarioTest::class
        Spec.configureScenario = null

        val failure = kotlin.test.assertFailsWith<IllegalStateException> {
            Spec.scenario()
                .When<String>("user adds the item") { }
                .then("cart shows the item", object : Assertion {
                    override val label = "cart shows the item"
                    override suspend fun run(context: SetupContext) {
                        error("timeout")
                    }
                })
                .run()
        }

        assertTrue(failure.message!!.contains("RejectsItem"))
        val html = readSuitePage(out)
        assertTrue(html.contains("1 FAILED"))
        assertTrue(html.contains("pre-error"))
        assertTrue(html.contains("data-title=\"RejectsItem\" open"))
    }

    @Test
    fun longTitlesDoNotShareADirectory() {
        val method = "UserFilledAllPropertyDetailsUpdatesThemProceedToTheNextStepAndValidatePayload"
        val mobile = ArtifactPaths.slug("$method[chromium-mobile $method]")
        val desktop = ArtifactPaths.slug("$method[chromium-desktop $method]")
        assertTrue(mobile != desktop)
        assertTrue(mobile.length <= 80)
    }
}

private fun readSuitePage(root: java.nio.file.Path): String {
    val index = Files.walk(root).use { paths ->
        paths.filter { it.fileName.toString() == "index.html" }.findFirst().orElseThrow()
    }
    return Files.readString(index)
}

private class RecordingBuilder : SystemUnderTestBuilder {
    val applied = mutableListOf<String>()
    var seen: String? = null
    private val context = object : SetupContext {}

    override suspend fun initialize() = Unit
    override fun createContext(): SetupContext = context
    override suspend fun apply(context: SetupContext, setup: Setup) {
        applied += setup.label
    }

    @Suppress("UNCHECKED_CAST")
    override fun <T : Any> construct(context: SetupContext, type: KClass<T>): T = "widget" as T

    override suspend fun dispose() = Unit
}

private class FailureRecordingBuilder(
    private val setupFailure: Throwable? = null,
) : SystemUnderTestBuilder, ScenarioTrace {
    var viewerEnsured = false
    private val context = object : SetupContext {}

    override suspend fun initialize() = Unit
    override fun createContext(): SetupContext = context
    override suspend fun apply(context: SetupContext, setup: Setup) {
        setupFailure?.let { throw it }
    }

    override fun <T : Any> construct(context: SetupContext, type: KClass<T>): T =
        error("This test does not construct page objects")

    override suspend fun dispose() = Unit
    override suspend fun startTrace() = Unit
    override suspend fun stopTrace(tracePath: Path) {
        Files.writeString(tracePath, "recorded trace")
    }

    override fun ensureViewer(artifactsRoot: Path) {
        viewerEnsured = true
    }
}
