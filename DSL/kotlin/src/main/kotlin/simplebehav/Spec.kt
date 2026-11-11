package simplebehav

import kotlin.reflect.KClass

/**
 * Entry point. Call [scenario] inside a test after a runner integration has called [Spec.configure].
 *
 * `when` is a Kotlin keyword, so the act step is [GivenToken.When].
 */
object Spec {
    var resolveVariant: (() -> String?)? = null
    var configureScenario: ((GivenToken) -> GivenToken)? = null

    var testName: String? = null
    var testClass: KClass<*>? = null

    private var adapter: SystemUnderTestBuilder? = null
    private var outDir: String? = null
    private var projectName: String = "tests"

    val baseUrl: String
        get() = System.getenv("SIMPLEBEHAV_BASE_URL") ?: "http://localhost:5173"

    fun configure(builder: SystemUnderTestBuilder, outDir: String, projectName: String? = null) {
        adapter = builder
        this.outDir = outDir
        if (!projectName.isNullOrBlank()) this.projectName = projectName
    }

    fun scenario(name: String = testName ?: error("[simplebehav] scenario name is missing")): GivenToken {
        val builder = adapter ?: error("[simplebehav] Call Spec.configure(builder, outDir) before scenario().")
        val variant = resolveVariant?.invoke()
        val scenarioName = if (variant == null) name else "$name[$variant]"
        val suitePath = ArtifactPaths.suitePath(projectName, testClass)
        val token = GivenToken(ScenarioRunner(outDir!!, suitePath, scenarioName, builder))
        return configureScenario?.invoke(token) ?: token
    }
}

class GivenToken internal constructor(@PublishedApi internal val runner: ScenarioRunner) {
    fun given(setup: Setup): GivenToken = and(setup)

    fun and(setup: Setup): GivenToken {
        runner.addSetup(setup)
        return this
    }

    fun arrange(setup: Setup): GivenToken = and(setup)

    inline fun <reified T : Any> When(label: String, noinline action: suspend (T) -> Unit): WhenToken {
        runner.setWhen(label) { context ->
            action(runner.adapter.construct(context, T::class))
        }
        return WhenToken(runner)
    }
}

class WhenToken @PublishedApi internal constructor(private val runner: ScenarioRunner) {
    fun then(label: String, assertion: Assertion): ThenToken = ThenToken(runner, label, assertion)
}

class ThenToken internal constructor(
    private val runner: ScenarioRunner,
    label: String,
    assertion: Assertion,
) {
    private val assertions = mutableListOf(label to assertion)

    fun and(label: String, assertion: Assertion): ThenToken {
        assertions += label to assertion
        return this
    }

    suspend fun run() = runner.execute(assertions)
}

@PublishedApi
internal class ScenarioRunner(
    private val outDir: String,
    private val suitePath: List<String>,
    private val scenarioName: String,
    @PublishedApi
    internal val adapter: SystemUnderTestBuilder,
) {
    private val setups = mutableListOf<Setup>()
    private var whenLabel: String = ""
    private var whenFn: (suspend (SetupContext) -> Unit)? = null

    fun addSetup(setup: Setup) {
        setups += setup
    }

    @PublishedApi
    internal fun setWhen(label: String, fn: suspend (SetupContext) -> Unit) {
        whenLabel = label
        whenFn = fn
    }

    suspend fun execute(assertions: List<Pair<String, Assertion>>) {
        val emitter = ArtifactEmitter(scenarioName)
        var status = "pass"
        var failureMessage: String? = null
        val context = adapter.createContext()
        val trace = adapter as? ScenarioTrace
        trace?.startTrace()
        try {
            runGiven(context, emitter)
            runWhen(context, emitter)
            runThen(context, assertions, emitter)
        } catch (ex: Exception) {
            status = "fail"
            failureMessage = ex.message
            emitter.add(
                ArtifactStep(StepType.Assert, "FAILED: $failureMessage", 0, failureMessage),
            )
        }

        val bundle = emitter.build(status)
        val dir = emitter.emit(bundle, outDir, suitePath)
        val suiteDir = dir.parent ?: error("[simplebehav] scenario directory has no suite parent")
        val traceViewerBase = "../".repeat(suitePath.size) + "trace-viewer/index.html"
        SuiteReporter.write(suiteDir, suitePath.last(), traceViewerBase)
        trace?.stopTrace(dir.resolve("trace.zip"))
        trace?.ensureViewer(java.nio.file.Path.of(outDir))

        if (status == "fail") {
            error("[simplebehav] \"$scenarioName\" failed: $failureMessage")
        }
    }

    private suspend fun runGiven(context: SetupContext, emitter: ArtifactEmitter) {
        if (setups.isEmpty()) return
        val started = System.currentTimeMillis()
        for (setup in setups) adapter.apply(context, setup)
        val label = setups.map { it.label }.filter { it.isNotEmpty() }.joinToString(", ")
        emitter.add(ArtifactStep(StepType.Setup, label.ifEmpty { "preconditions applied" }, System.currentTimeMillis() - started))
    }

    private suspend fun runWhen(context: SetupContext, emitter: ArtifactEmitter) {
        val fn = whenFn ?: return
        val started = System.currentTimeMillis()
        fn(context)
        emitter.add(ArtifactStep(StepType.Act, whenLabel, System.currentTimeMillis() - started))
    }

    private suspend fun runThen(
        context: SetupContext,
        assertions: List<Pair<String, Assertion>>,
        emitter: ArtifactEmitter,
    ) {
        for ((label, assertion) in assertions) {
            val started = System.currentTimeMillis()
            assertion.run(context)
            emitter.add(ArtifactStep(StepType.Assert, label, System.currentTimeMillis() - started))
        }
    }
}
