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

class ScenarioFailureTest {
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
    fun reportFailureStillStopsTheTraceAndPreservesTheAssertionFailure() = runBlocking {
        val out = Files.createTempDirectory("simplebehav-report-failure")
        Files.createDirectories(out.resolve("suite/index.html"))
        val original = AssertionError("assertion failed")
        val adapter = FailureRecordingBuilder()

        val failure = assertFailsWith<AssertionError> {
            failingRunner(out, adapter, original).execute(emptyList())
        }

        assertSame(original, failure)
        assertTrue(failure.suppressed.isNotEmpty(), "The report failure must not replace the assertion")
        assertTrue(Files.exists(out.resolve("suite/scenario/trace.zip")))
        assertTrue(adapter.viewerEnsured)
    }

    @Test
    fun traceFailureDoesNotReplaceTheAssertionFailure() = runBlocking {
        val out = Files.createTempDirectory("simplebehav-trace-failure")
        val original = AssertionError("assertion failed")
        val traceFailure = IllegalStateException("trace failed")
        val adapter = FailureRecordingBuilder(traceFailure = traceFailure)

        val failure = assertFailsWith<AssertionError> {
            failingRunner(out, adapter, original).execute(emptyList())
        }

        assertSame(original, failure)
        assertTrue(failure.suppressed.contains(traceFailure))
        assertTrue(adapter.viewerEnsured)
    }

    @Test
    fun traceFailureFailsAnOtherwisePassingScenario() = runBlocking {
        val out = Files.createTempDirectory("simplebehav-passing-trace-failure")
        val original = IllegalStateException("trace failed")
        val adapter = FailureRecordingBuilder(traceFailure = original)
        val runner = ScenarioRunner(out.toString(), listOf("suite"), "scenario", adapter)

        val failure = assertFailsWith<IllegalStateException> {
            runner.execute(emptyList())
        }

        assertSame(original, failure)
        assertTrue(adapter.viewerEnsured)
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
}

private class FailureRecordingBuilder(
    private val setupFailure: Throwable? = null,
    private val traceFailure: Throwable? = null,
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
        traceFailure?.let { throw it }
    }

    override fun ensureViewer(artifactsRoot: Path) {
        viewerEnsured = true
    }
}
