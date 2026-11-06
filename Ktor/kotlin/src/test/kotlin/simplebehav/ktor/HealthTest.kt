package simplebehav.ktor

import io.ktor.client.request.get
import io.ktor.client.statement.bodyAsText
import io.ktor.server.response.respondText
import io.ktor.server.routing.get
import io.ktor.server.routing.routing
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlinx.coroutines.runBlocking

class HealthTest {
    @Test
    fun healthRespondsOk() = runBlocking {
        val builder = KtorBuilder {
            routing {
                get("/health") { call.respondText("ok") }
            }
        }
        builder.initialize()
        try {
            val client = builder.construct(builder.createContext(), io.ktor.client.HttpClient::class)
            val body = client.get("${builder.baseUrl}/health").bodyAsText()
            assertEquals("ok", body)
        } finally {
            builder.dispose()
        }
    }
}
