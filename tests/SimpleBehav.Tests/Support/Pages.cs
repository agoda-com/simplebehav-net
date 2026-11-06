namespace SimpleBehav.Tests.Support;

/// <summary>Preconditions for the demo suites — each one mocks a page the scenarios open.</summary>
public static class Pages
{
    public static ISetup Checkout { get; } = new MockedPage(
        "checkout page is available",
        "**/checkout",
        """
        <body style="font-family: sans-serif; padding: 2rem">
          <h1>Checkout</h1>
          <label>Card <input data-testid="card" value="4111 1111 1111 1111"></label>
          <label>Currency <input data-testid="currency" value="USD"></label>
          <p id="total"></p>
          <button data-testid="submit">Place order</button>
          <div id="result"></div>
          <script>
            const card = document.querySelector('[data-testid=card]');
            const currency = document.querySelector('[data-testid=currency]');
            const submit = document.querySelector('[data-testid=submit]');
            const total = document.getElementById('total');
            const sync = () => {
              submit.disabled = card.value.trim() === '';
              total.textContent = 'Total: ' + (currency.value || 'USD') + ' 128.00';
            };
            card.addEventListener('input', sync);
            currency.addEventListener('input', sync);
            submit.addEventListener('click', () => {
              document.getElementById('result').innerHTML =
                '<h2>Order confirmed</h2><p>Order AG-40128</p>';
            });
            sync();
          </script>
        </body>
        """);

    public static ISetup Allotment { get; } = new MockedPage(
        "allotment page is available",
        "**/allotment",
        """
        <body style="font-family: sans-serif; padding: 2rem">
          <h1>Allotment</h1>
          <label>Rooms <input data-testid="rooms" value="4"></label>
          <button data-testid="save">Save</button>
          <div id="toast"></div>
          <script>
            const rooms = document.querySelector('[data-testid=rooms]');
            document.querySelector('[data-testid=save]').addEventListener('click', () => {
              const value = Number(rooms.value);
              document.getElementById('toast').textContent = value > 10
                ? 'Allotment exceeds contracted rooms.'
                : 'Save completed.';
            });
          </script>
        </body>
        """);

    private sealed class MockedPage(string label, string pattern, string html) : IPlaywrightSetup
    {
        public string Label { get; } = label;

        public void Apply(SetupContext ctx) =>
            ctx.MockRoute(pattern, route => route.FulfillAsync(new()
            {
                ContentType = "text/html",
                Body = html,
            }));
    }
}
