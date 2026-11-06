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
        val wroteBundle = Files.walk(out).use { paths ->
            paths.anyMatch { it.fileName.toString() == "bundle.json" }
        }
        assertTrue(wroteBundle)
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
