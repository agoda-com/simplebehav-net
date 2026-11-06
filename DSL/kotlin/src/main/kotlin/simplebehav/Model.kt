package simplebehav

import kotlin.reflect.KClass

/** An opaque precondition. The adapter decides what it means for the system under test. */
interface Setup {
    val label: String
}

/** Adapter-owned context for one scenario. The DSL passes it through and does not inspect it. */
interface SetupContext

/**
 * Pluggable builder for the system under test. One implementation per system type
 * (Playwright, Ktor, a DI host). The DSL drives the lifecycle.
 */
interface SystemUnderTestBuilder {
    suspend fun initialize()
    fun createContext(): SetupContext
    suspend fun apply(context: SetupContext, setup: Setup)
    fun <T : Any> construct(context: SetupContext, type: KClass<T>): T
    suspend fun dispose()
}

/** Optional. A browser adapter records a trace around the scenario. */
interface ScenarioTrace {
    suspend fun startTrace()
    suspend fun stopTrace(tracePath: java.nio.file.Path)
    fun ensureViewer(artifactsRoot: java.nio.file.Path)
}

/** A Then-step. The assertion knows how to read the adapter context. */
interface Assertion {
    val label: String
    suspend fun run(context: SetupContext)
}

fun setup(label: String, action: suspend (SetupContext) -> Unit): Setup = ActionSetup(label, action)

private class ActionSetup(
    override val label: String,
    private val action: suspend (SetupContext) -> Unit,
) : Setup {
    suspend fun apply(context: SetupContext) = action(context)
}
