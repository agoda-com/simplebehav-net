package simplebehav

import java.nio.file.Files
import kotlin.reflect.KClass
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue
import kotlinx.coroutines.runBlocking

class ScenarioTest {
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
