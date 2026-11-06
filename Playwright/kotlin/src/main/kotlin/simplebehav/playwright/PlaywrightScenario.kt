package simplebehav.playwright

import java.util.concurrent.ConcurrentHashMap
import org.junit.jupiter.api.extension.AfterAllCallback
import org.junit.jupiter.api.extension.AfterEachCallback
import org.junit.jupiter.api.extension.BeforeAllCallback
import org.junit.jupiter.api.extension.BeforeEachCallback
import org.junit.jupiter.api.extension.ExtendWith
import org.junit.jupiter.api.extension.ExtensionContext
import simplebehav.GivenToken
import simplebehav.Spec

/**
 * JUnit 5 counterpart of the NUnit `[PlaywrightScenario]` attribute.
 * Implement [ScenarioDefaults] on the test class to pre-fill every scenario.
 */
@Target(AnnotationTarget.CLASS)
@ExtendWith(PlaywrightScenarioExtension::class)
annotation class PlaywrightScenario

interface ScenarioDefaults {
    fun configure(token: GivenToken): GivenToken
}

class PlaywrightScenarioExtension :
    BeforeAllCallback,
    AfterAllCallback,
    BeforeEachCallback,
    AfterEachCallback {
    override fun beforeAll(context: ExtensionContext) {
        sessions[context.requiredTestClass] = PlaywrightSession.fromEnvironment()
    }

    override fun afterAll(context: ExtensionContext) {
        sessions.remove(context.requiredTestClass)?.close()
    }

    override fun beforeEach(context: ExtensionContext) {
        val session = sessions.getValue(context.requiredTestClass)
        val page = session.newPage()
        pages[context.uniqueId] = page
        val artifacts = System.getenv("SIMPLEBEHAV_ARTIFACTS") ?: "artifacts"
        Spec.configure(PlaywrightBuilder(page), artifacts, context.requiredTestClass.simpleName)
        Spec.testName = context.requiredTestMethod.name
        Spec.testClass = context.requiredTestClass.kotlin
        val instance = context.requiredTestInstance
        Spec.configureScenario = if (instance is ScenarioDefaults) instance::configure else null
    }

    override fun afterEach(context: ExtensionContext) {
        pages.remove(context.uniqueId)?.close()
        Spec.configureScenario = null
    }

    private companion object {
        val sessions = ConcurrentHashMap<Class<*>, PlaywrightSession>()
        val pages = ConcurrentHashMap<String, com.microsoft.playwright.Page>()
    }
}
