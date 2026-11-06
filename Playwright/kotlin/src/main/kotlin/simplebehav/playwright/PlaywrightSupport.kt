package simplebehav.playwright

import com.microsoft.playwright.Browser
import com.microsoft.playwright.Locator
import com.microsoft.playwright.Page
import com.microsoft.playwright.Playwright
import com.microsoft.playwright.Route
import com.microsoft.playwright.Tracing
import com.microsoft.playwright.options.WaitForSelectorState
import java.nio.file.Files
import java.nio.file.Path
import java.nio.file.StandardCopyOption
import kotlin.reflect.KClass
import simplebehav.ScenarioTrace
import simplebehav.Setup
import simplebehav.SetupContext
import simplebehav.SystemUnderTestBuilder

class PlaywrightSetupContext(val page: Page) : SetupContext {
    private val routes = mutableListOf<Pair<String, (Route) -> Unit>>()
    private val scripts = mutableListOf<String>()

    fun mockRoute(pattern: String, handler: (Route) -> Unit) {
        routes += pattern to handler
    }

    fun addInitScript(script: String) {
        scripts += script
    }

    fun applyToPage() {
        scripts.forEach { page.addInitScript(it) }
        routes.forEach { (pattern, handler) -> page.route(pattern, handler) }
    }
}

interface PlaywrightSetup : Setup {
    fun apply(context: PlaywrightSetupContext)
}

class PlaywrightBuilder(private val page: Page) : SystemUnderTestBuilder, ScenarioTrace {
    override suspend fun initialize() = Unit

    override fun createContext(): SetupContext = PlaywrightSetupContext(page)

    override suspend fun apply(context: SetupContext, setup: Setup) {
        val playwrightContext = context as? PlaywrightSetupContext
            ?: error("PlaywrightBuilder requires PlaywrightSetupContext, got ${context::class.simpleName}")
        val playwrightSetup = setup as? PlaywrightSetup
            ?: error("PlaywrightBuilder cannot apply ${setup::class.simpleName}. Implement PlaywrightSetup.")
        playwrightSetup.apply(playwrightContext)
        playwrightContext.applyToPage()
    }

    @Suppress("UNCHECKED_CAST")
    override fun <T : Any> construct(context: SetupContext, type: KClass<T>): T {
        if (type == Page::class) return page as T
        val ctor = type.java.getConstructor(Page::class.java)
        return ctor.newInstance(page) as T
    }

    override suspend fun dispose() = Unit

    override suspend fun startTrace() {
        page.context().tracing().start(
            Tracing.StartOptions().setScreenshots(true).setSnapshots(true).setSources(false),
        )
    }

    override suspend fun stopTrace(tracePath: Path) {
        Files.createDirectories(tracePath.parent)
        page.context().tracing().stop(Tracing.StopOptions().setPath(tracePath))
    }

    override fun ensureViewer(artifactsRoot: Path) {
        val destination = artifactsRoot.resolve("trace-viewer")
        if (Files.exists(destination.resolve("index.html"))) return
        val source = traceViewerInPlaywrightJar()
        Files.walk(source).use { paths ->
            paths.filter { Files.isRegularFile(it) }.forEach { file ->
                val relative = source.relativize(file).toString()
                val target = destination.resolve(relative)
                Files.createDirectories(target.parent)
                Files.copy(file, target, StandardCopyOption.REPLACE_EXISTING)
            }
        }
    }

    private fun traceViewerInPlaywrightJar(): Path {
        val folder = playwrightDriverFolder()
        val resource = "driver/$folder/package/lib/vite/traceViewer/index.html"
        val url = Playwright::class.java.classLoader.getResource(resource)
            ?: error("Playwright Trace Viewer assets were not found at $resource")
        val connection = url.openConnection() as java.net.JarURLConnection
        val filesystem = java.nio.file.FileSystems.newFileSystem(
            java.net.URI.create("jar:${connection.jarFileURL}"),
            emptyMap<String, Any>(),
        )
        return filesystem.getPath("driver", folder, "package", "lib", "vite", "traceViewer")
    }

    private fun playwrightDriverFolder(): String {
        val os = System.getProperty("os.name").lowercase()
        val arch = System.getProperty("os.arch").lowercase()
        val arm = arch == "aarch64" || arch == "arm64"
        return when {
            os.contains("mac") -> if (arm) "mac-arm64" else "mac"
            os.contains("win") -> "win32_x64"
            arm -> "linux-arm64"
            else -> "linux"
        }
    }
}

object See {
    fun textVisible(partialText: String, timeoutMs: Double = 10_000.0) = pageAssertion(
        "\"$partialText\" visible on page",
    ) { page ->
        page.getByText(partialText, Page.GetByTextOptions().setExact(false))
            .waitFor(Locator.WaitForOptions().setState(WaitForSelectorState.VISIBLE).setTimeout(timeoutMs))
    }

    fun elementVisible(selector: String, timeoutMs: Double = 10_000.0) = pageAssertion(
        "element \"$selector\" visible",
    ) { page ->
        page.locator(selector)
            .waitFor(Locator.WaitForOptions().setState(WaitForSelectorState.VISIBLE).setTimeout(timeoutMs))
    }

    fun buttonDisabled(testId: String, timeoutMs: Double = 5_000.0) = pageAssertion(
        "button [$testId] is disabled",
    ) { page ->
        val button = page.getByTestId(testId)
        button.waitFor(Locator.WaitForOptions().setState(WaitForSelectorState.VISIBLE).setTimeout(timeoutMs))
        if (!button.isDisabled) error("Expected button [$testId] to be disabled but it was enabled")
    }

    private fun pageAssertion(label: String, check: (Page) -> Unit) = object : simplebehav.Assertion {
        override val label = label
        override suspend fun run(context: SetupContext) {
            val page = (context as? PlaywrightSetupContext)?.page
                ?: error("See assertions require a Playwright setup context")
            check(page)
        }
    }
}

/** One Chromium browser per test class, one page per test. Same lifecycle as [PlaywrightScenario] on .NET. */
class PlaywrightSession(private val headless: Boolean) : AutoCloseable {
    private val playwright: Playwright = Playwright.create()
    private val browser: Browser = playwright.chromium().launch(
        com.microsoft.playwright.BrowserType.LaunchOptions().setHeadless(headless),
    )

    fun newPage(): Page = browser.newPage()

    override fun close() {
        browser.close()
        playwright.close()
    }

    companion object {
        fun fromEnvironment(): PlaywrightSession {
            val headed = System.getenv("SIMPLEBEHAV_HEADLESS").equals("false", ignoreCase = true)
            return PlaywrightSession(headless = !headed)
        }
    }
}
