package simplebehav.ktor

import io.ktor.client.HttpClient
import io.ktor.client.engine.cio.CIO as ClientCIO
import io.ktor.server.application.Application
import io.ktor.server.cio.CIO
import io.ktor.server.engine.EmbeddedServer
import io.ktor.server.engine.embeddedServer
import kotlin.reflect.KClass
import simplebehav.Setup
import simplebehav.SetupContext
import simplebehav.SystemUnderTestBuilder

/**
 * Kotlin host adapter. Starts one embedded Ktor server for the fixture and hands the
 * scenario an [HttpClient] pointed at it. This is the Ktor-shaped counterpart of the
 * .NET dependency-injection page builder: the DSL stays the same, the system under test changes.
 */
class KtorBuilder(private val module: Application.() -> Unit) : SystemUnderTestBuilder {
    private var server: EmbeddedServer<*, *>? = null
    private var client: HttpClient? = null
    private val context = object : SetupContext {}

    val baseUrl: String
        get() = "http://127.0.0.1:$port"

    private var port: Int = 0

    override suspend fun initialize() {
        if (server != null) return
        val started = embeddedServer(CIO, port = 0, module = module).start(wait = false)
        server = started
        port = started.engine.resolvedConnectors().first().port
        client = HttpClient(ClientCIO) {
            expectSuccess = true
        }
    }

    override fun createContext(): SetupContext = context

    override suspend fun apply(context: SetupContext, setup: Setup) = Unit

    @Suppress("UNCHECKED_CAST")
    override fun <T : Any> construct(context: SetupContext, type: KClass<T>): T {
        if (type == HttpClient::class) return client as T
        error("KtorBuilder cannot construct ${type.simpleName}")
    }

    override suspend fun dispose() {
        client?.close()
        client = null
        server?.stop(gracePeriodMillis = 100, timeoutMillis = 1_000)
        server = null
    }
}
