import { chromium, type Page, type Route } from "playwright";
import { Spec, type Setup } from "simplebehav";
import test from "node:test";
import { PlaywrightBuilder, PlaywrightSetupContext, See, type PlaywrightSetup } from "../src/index.ts";

class CheckoutPage {
  constructor(private readonly page: Page) {}

  async submit(): Promise<void> {
    await this.page.goto("https://simplebehav.test/checkout");
    await this.page.getByTestId("submit").click();
  }
}

const checkoutPage: Setup & PlaywrightSetup = {
  label: "checkout page is available",
  apply(context: PlaywrightSetupContext) {
    context.mockRoute("**/checkout", (route: Route) =>
      route.fulfill({
        contentType: "text/html",
        body: `<body>
          <button data-testid="submit">Place order</button>
          <div id="result"></div>
          <script>
            document.querySelector('[data-testid=submit]').addEventListener('click', () => {
              document.getElementById('result').textContent = 'Order confirmed';
            });
          </script>
        </body>`,
      }),
    );
  },
};

test("place order shows confirmation", async () => {
  const browser = await chromium.launch({ headless: true });
  const page = await browser.newPage();
  try {
    Spec.configure(new PlaywrightBuilder(page), "artifacts", "CheckoutTest");
    Spec.testName = "placeOrderShowsConfirmation";
    Spec.suite = ["CheckoutTest"];
    Spec.configureScenario = undefined;
    await Spec.scenario()
      .given(checkoutPage)
      .when("user places the order", CheckoutPage, (checkout) => checkout.submit())
      .then("order confirmation is shown", See.textVisible("Order confirmed"))
      .run();
  } finally {
    await page.close();
    await browser.close();
  }
});
