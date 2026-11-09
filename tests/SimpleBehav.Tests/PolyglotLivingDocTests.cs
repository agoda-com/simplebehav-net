using SimpleBehav.SpecGen;

namespace SimpleBehav.Tests;

public class PolyglotLivingDocTests
{
    private const string Kotlin = """
        @PlaywrightScenario
        class SampleTests {
            @Test
            fun plainScenario() = runBlocking {
                // .then("ghost", x)
                Spec.scenario()
                    .given(Pages.Checkout)
                    .When<CheckoutPage>("user acts") { page -> page.submit() }
                    .then("outcome one", See.textVisible("x"))
                    .and("say .then(\"nope\")", See.textVisible("y"))
            }

            @ProvesBug
            @ParameterizedTest
            @ValueSource(strings = ["USD", "THB"])
            fun parameterisedScenario(currency: String) = runBlocking {
                Spec.scenario().given(Pages.Checkout).When("acts") { it.go() }.then("done", See.textVisible("z")).run()
            }

            @Test
            fun unitTestWithoutChain() { }
        }
        """;

    private const string TypeScript = """
        test("plain scenario", async () => {
          // .then("ghost", x)
          await Spec.scenario()
            .given(Pages.Checkout)
            .when("user acts", CheckoutPage, (page) => page.submit())
            .then("outcome one", See.textVisible("x"))
            .and("outcome two", See.textVisible("y"));
        });

        test("parameterised scenario [ProvesBug]", async () => {
          await Spec.scenario().given(Pages.Checkout).when("acts", (p) => p.go()).then("done", See.textVisible("z")).run();
        });

        test("unit test without a chain", () => {
          assert.equal(1, 1);
        });
        """;

    [Test]
    public void ParseKotlin_ReadsTheChain_AndJUnitCases()
    {
        var scenarios = SpecParser.ParseKotlin(Kotlin);

        var plain = scenarios.Single(s => s.MethodName == "plainScenario");
        Assert.That(plain.Steps.Select(s => s.Type), Is.EqualTo(new[] { "Given", "When", "Then", "And" }));
        Assert.That(plain.Steps.Select(s => s.Label),
            Is.EqualTo(new[] { "Pages.Checkout", "user acts", "outcome one", "say .then(\"nope\")" }));
        Assert.That(plain.Steps.Single(s => s.Type == "When").TypeArg, Is.EqualTo("CheckoutPage"));
        Assert.That(plain.IsProvesBug, Is.False);

        var parameterised = scenarios.Single(s => s.MethodName == "parameterisedScenario");
        Assert.That(parameterised.IsProvesBug, Is.True);
        Assert.That(parameterised.Cases, Is.EqualTo(new[] { "USD", "THB" }));
        Assert.That(parameterised.Steps.Single(s => s.Type == "When").TypeArg, Is.Null);
        Assert.That(scenarios.Any(s => s.MethodName == "unitTestWithoutChain"), Is.True);
        Assert.That(scenarios.Single(s => s.MethodName == "unitTestWithoutChain").Steps, Is.Empty);
    }

    [Test]
    public void ParseTypeScript_ReadsTheChain_AndTheTestTitle()
    {
        var scenarios = SpecParser.ParseTypeScript(TypeScript);

        var plain = scenarios.Single(s => s.MethodName == "PlainScenario");
        Assert.That(plain.Steps.Select(s => s.Type), Is.EqualTo(new[] { "Given", "When", "Then", "And" }));
        Assert.That(plain.Steps.Select(s => s.Label),
            Is.EqualTo(new[] { "Pages.Checkout", "user acts", "outcome one", "outcome two" }));
        Assert.That(plain.Steps.Single(s => s.Type == "When").TypeArg, Is.EqualTo("CheckoutPage"));

        var proves = scenarios.Single(s => s.MethodName == "ParameterisedScenario");
        Assert.That(proves.IsProvesBug, Is.True);
        Assert.That(proves.Steps.Single(s => s.Type == "When").TypeArg, Is.Null);
        Assert.That(GraphEmitter.ResolvePage(proves), Is.EqualTo("Pages.Checkout"));
        Assert.That(scenarios.Single(s => s.MethodName == "UnitTestWithoutAChain").Steps, Is.Empty);
    }

    [Test]
    public async Task LivingDoc_ReadsKotlinAndTypeScriptFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var tests = Path.Combine(root, "tests");
        Directory.CreateDirectory(Path.Combine(tests, "kotlin"));
        Directory.CreateDirectory(Path.Combine(tests, "typescript"));
        await File.WriteAllTextAsync(Path.Combine(tests, "kotlin", "SampleTests.kt"), Kotlin);
        await File.WriteAllTextAsync(Path.Combine(tests, "typescript", "checkout.test.ts"), TypeScript);
        var outDir = Path.Combine(root, "out");

        try
        {
            var features = await LivingDocEmitter.LoadFeaturesAsync(tests);
            await LivingDocEmitter.EmitAsync(features, outDir, null, SuiteManifest.Build(features, null), null);

            Assert.That(features.Select(f => f.SourceExtension), Is.EquivalentTo(new[] { ".kt", ".ts" }));

            var kotlinPage = await File.ReadAllTextAsync(Path.Combine(outDir, "kotlin", "SampleTests", "index.html"));
            Assert.That(kotlinPage, Does.Contain("kotlin/SampleTests.kt").And.Contain("user acts").And.Contain("USD"));
            Assert.That(kotlinPage, Does.Not.Contain("unit Test Without Chain").And.Not.Contain("ghost"));

            var typescriptPage = await File.ReadAllTextAsync(Path.Combine(outDir, "typescript", "checkout.test", "index.html"));
            Assert.That(typescriptPage, Does.Contain("typescript/checkout.test.ts").And.Contain("Plain Scenario").And.Contain("proves bug"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void ParseFile_ReadsTheShippedCheckoutScenarios()
    {
        var root = FindRepoRoot();
        var kotlin = File.ReadAllText(Path.Combine(root, "Playwright", "kotlin", "src", "test", "kotlin", "simplebehav", "playwright", "CheckoutTest.kt"));
        var typescript = File.ReadAllText(Path.Combine(root, "Playwright", "typescript", "test", "checkout.browser.test.ts"));

        var kotlinScenario = SpecParser.ParseFile("CheckoutTest.kt", kotlin).Single(s => s.Steps.Count > 0);
        Assert.That(kotlinScenario.MethodName, Is.EqualTo("placeOrderShowsConfirmation"));
        Assert.That(kotlinScenario.Steps.Select(s => $"{s.Type}:{s.Label}"), Is.EqualTo(new[]
        {
            "Given:checkoutPage",
            "When:user places the order",
            "Then:order confirmation is shown",
        }));
        Assert.That(kotlinScenario.Steps.Single(s => s.Type == "When").TypeArg, Is.EqualTo("CheckoutPage"));

        var typescriptScenario = SpecParser.ParseFile("checkout.browser.test.ts", typescript).Single(s => s.Steps.Count > 0);
        Assert.That(typescriptScenario.MethodName, Is.EqualTo("PlaceOrderShowsConfirmation"));
        Assert.That(typescriptScenario.Steps.Select(s => $"{s.Type}:{s.Label}"), Is.EqualTo(new[]
        {
            "Given:checkoutPage",
            "When:user places the order",
            "Then:order confirmation is shown",
        }));
        Assert.That(typescriptScenario.Steps.Single(s => s.Type == "When").TypeArg, Is.EqualTo("CheckoutPage"));
    }

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "SimpleBehav.sln")))
                return dir.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate SimpleBehav.sln from the test directory.");
    }
}
