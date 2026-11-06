package simplebehav.playwright

import com.microsoft.playwright.Page
import com.microsoft.playwright.Route
import kotlinx.coroutines.runBlocking
import org.junit.jupiter.api.Tag
import org.junit.jupiter.api.Test
import simplebehav.Setup
import simplebehav.Spec

@Tag("browser")
@PlaywrightScenario
class CheckoutTest {
    @Test
    fun placeOrderShowsConfirmation() = runBlocking {
        Spec.scenario()
            .given(checkoutPage)
            .When<CheckoutPage>("user places the order") { it.submit() }
            .then("order confirmation is shown", See.textVisible("Order confirmed"))
            .run()
    }
}

private class CheckoutPage(private val page: Page) {
    fun submit() {
        page.navigate("https://simplebehav.test/checkout")
        page.getByTestId("submit").click()
    }
}

private val checkoutPage: Setup = object : PlaywrightSetup {
    override val label = "checkout page is available"
    override fun apply(context: PlaywrightSetupContext) {
        context.mockRoute("**/checkout") { route: Route ->
            route.fulfill(
                Route.FulfillOptions()
                    .setContentType("text/html")
                    .setBody(
                        """
                        <body>
                          <button data-testid="submit">Place order</button>
                          <div id="result"></div>
                          <script>
                            document.querySelector('[data-testid=submit]').addEventListener('click', () => {
                              document.getElementById('result').textContent = 'Order confirmed';
                            });
                          </script>
                        </body>
                        """.trimIndent(),
                    ),
            )
        }
    }
}
