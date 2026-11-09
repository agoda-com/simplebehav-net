using SimpleBehav.SpecGen;

namespace SimpleBehav.Tests;

public class LivingDocTests
{
    private const string Source = """
        public class SampleTests
        {
            [Test]
            public async Task Plain_Scenario()
            {
                await Scenario()
                    .Given(Pages.Checkout)
                    .When<CheckoutPage>("user acts", page => page.Submit())
                    .Then("outcome one", See.TextVisible("x"))
                    .And("outcome two", See.TextVisible("y"));
            }

            [TestCase("USD")]
            [TestCase("THB")]
            public async Task Parameterised_Scenario(string currency)
            {
                await Scenario().Given(Pages.Checkout).When("acts", p => p.Go()).Then("done", See.TextVisible("z"));
            }

            [Test]
            public void Unit_Test_Without_Chain() { }
        }
        """;

    [Test]
    public void ParseTestSource_ReturnsStepsInSourceOrder()
    {
        var scenario = SpecParser.ParseTestSource(Source).First(s => s.MethodName == "Plain_Scenario");

        Assert.That(scenario.Steps.Select(s => s.Type), Is.EqualTo(new[] { "Given", "When", "Then", "And" }));
        Assert.That(scenario.Steps.Select(s => s.Label),
            Is.EqualTo(new[] { "Pages.Checkout", "user acts", "outcome one", "outcome two" }));
        Assert.That(scenario.Steps.Single(s => s.Type == "When").TypeArg, Is.EqualTo("CheckoutPage"));
    }

    [Test]
    public void ResolvePage_PrefersWhenType_FallingBackToGivenSetup()
    {
        var withPage = SpecParser.ParseTestSource(Source).First(s => s.MethodName == "Plain_Scenario");
        Assert.That(GraphEmitter.ResolvePage(withPage), Is.EqualTo("CheckoutPage"));

        var withoutPage = SpecParser.ParseTestSource(Source).First(s => s.MethodName == "Parameterised_Scenario");
        Assert.That(GraphEmitter.ResolvePage(withoutPage), Is.EqualTo("Pages.Checkout"));
    }

    [Test]
    public void ParseTestSource_IncludesTestCaseMethodsWithTheirCases()
    {
        var scenario = SpecParser.ParseTestSource(Source).Single(s => s.MethodName == "Parameterised_Scenario");

        Assert.That(scenario.Cases, Is.EqualTo(new[] { "USD", "THB" }));
    }

    [Test]
    public async Task LivingDoc_WritesPagePerTestFile_AndSkipsStepLessTests()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var tests = Path.Combine(root, "tests", "Booking");
        Directory.CreateDirectory(tests);
        await File.WriteAllTextAsync(Path.Combine(tests, "SampleTests.cs"), Source);
        var outDir = Path.Combine(root, "out");

        try
        {
            var features = await LivingDocEmitter.LoadFeaturesAsync(Path.Combine(root, "tests"));
            await LivingDocEmitter.EmitAsync(features, outDir, "../reports", SuiteManifest.Build(features, null), null);

            var page = await File.ReadAllTextAsync(Path.Combine(outDir, "Booking", "SampleTests", "index.html"));
            Assert.That(page, Does.Contain("Plain Scenario").And.Contain("outcome two"));
            Assert.That(page, Does.Not.Contain("Unit Test Without Chain"));
            Assert.That(page, Does.Contain("id=\"trace-viewer\"").And.Contain("../../../reports/Booking/SampleTests/"));
            Assert.That(File.Exists(Path.Combine(outDir, "index.html")));
            Assert.That(File.Exists(Path.Combine(outDir, "suite.json")));

            var index = await File.ReadAllTextAsync(Path.Combine(outDir, "index.html"));
            Assert.That(index, Does.Contain("href=\"#Booking_SampleTests_Plain_Scenario\"").And.Contain("id=\"trace-viewer\""));

            var graph = await File.ReadAllTextAsync(Path.Combine(outDir, GraphEmitter.FileName));
            Assert.That(graph, Does.Contain("flowchart LR"));
            Assert.That(graph, Does.Contain("CheckoutPage"));
            Assert.That(graph, Does.Contain("Pages.Checkout"));
            Assert.That(graph, Does.Contain("user acts"));
            Assert.That(graph, Does.Contain("outcome one"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task Graph_FromDemoSuite_GroupsByPageObject_LeftToRight()
    {
        // Example: Booking (CheckoutPage) + Inventory (Pages.Allotment via When<IPage>).
        var root = FindRepoRoot();
        var features = await LivingDocEmitter.LoadFeaturesAsync(Path.Combine(root, "tests", "SimpleBehav.Tests"));
        var behaviour = features.Where(f => f.Name is "CheckoutTests" or "AllotmentTests").ToList();
        Assert.That(behaviour, Has.Count.EqualTo(2));

        var graph = GraphEmitter.Emit(behaviour);
        Assert.That(graph, Does.Contain("flowchart LR")
            .And.Contain("CheckoutPage")
            .And.Contain("Pages.Checkout")
            .And.Contain("Pages.Allotment")
            .And.Contain("user places the order")
            .And.Contain("user saves the allotment"));
        // Keep above Mermaid defaults so large consumer suites can render before page sharding.
        Assert.That(graph, Does.Contain("maxTextSize: 500000").And.Contain("maxEdges: 5000"));
    }

    [Test]
    public void Graph_FeatureNodes_LinkToTraceView_OrFallBackToFeaturePage()
    {
        var feature = new Feature("SampleTests", "Booking",
            SpecParser.ParseTestSource(Source));
        var withReports = GraphEmitter.Emit(new[] { feature }, "reports");
        Assert.That(withReports, Does.Contain("click F_Booking_SampleTests href \"reports/Booking/SampleTests/\""));

        var withoutReports = GraphEmitter.Emit(new[] { feature });
        Assert.That(withoutReports, Does.Contain("click F_Booking_SampleTests href \"Booking/SampleTests/\""));
    }

    [Test]
    public void Graph_TopBar_LinksEverySection_AndMarksCurrent()
    {
        var feature = new Feature("SampleTests", "Booking", SpecParser.ParseTestSource(Source));
        var graph = GraphEmitter.Emit(new[] { feature }, "reports");
        Assert.That(graph, Does.Contain("href=\"index.html\"")
            .And.Contain($"href=\"{GraphEmitter.FileName}\" class=\"active\"")
            .And.Contain("href=\"reports/\""));
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

    private static void WriteBundle(string root, string suite, string slug, string title, string status, string? error = null)
    {
        var dir = Path.Combine(root, "My.Tests", suite, slug);
        Directory.CreateDirectory(dir);
        var step = error is null ? "{ \"type\": \"Act\", \"label\": \"x\", \"durationMs\": 1 }"
            : $"{{ \"type\": \"Assert\", \"label\": \"x\", \"durationMs\": 1, \"error\": \"{error}\" }}";
        File.WriteAllText(Path.Combine(dir, "bundle.json"),
            $"{{ \"id\": \"1\", \"title\": \"{title}\", \"status\": \"{status}\", \"durationMs\": 10, \"steps\": [ {step} ] }}");
    }

    private static IReadOnlyList<Feature> SampleFeatures() =>
        [new Feature("SampleTests", "Booking", SpecParser.ParseTestSource(Source).Where(s => s.Steps.Count > 0).ToList())];

    [Test]
    public async Task ScenarioClick_CarriesItsTraceBesideTheSpec()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        WriteBundle(root, "Booking/SampleTests", "plain", "Plain_Scenario", "pass");
        var outDir = Path.Combine(root, "livingdoc");
        var runs = await ResultsLoader.LoadAsync(root);

        try
        {
            await LivingDocEmitter.EmitAsync(SampleFeatures(), outDir, "../reports", SuiteManifest.Build(SampleFeatures(), runs), null, runs);
            var page = await File.ReadAllTextAsync(Path.Combine(outDir, "index.html"));
            Assert.That(page, Does.Contain("data-slug=\"plain\"").And.Contain("outcome two").And.Contain("trace-viewer/index.html"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task Results_AreMatchedToScenarios_AndVariantsAreAggregated()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        WriteBundle(root, "Booking/SampleTests", "plain", "Plain_Scenario", "pass");
        WriteBundle(root, "Booking/SampleTests", "usd", "Parameterised_Scenario[USD]", "pass");
        WriteBundle(root, "Booking/SampleTests", "thb", "Parameterised_Scenario[THB]", "fail", "total missing");

        try
        {
            var manifest = SuiteManifest.Build(SampleFeatures(), await ResultsLoader.LoadAsync(root));
            var scenarios = manifest.Features.Single().Scenarios.ToDictionary(s => s.Id);

            Assert.That(scenarios["Booking/SampleTests/Plain_Scenario"].Status, Is.EqualTo(Status.Pass));
            var variants = scenarios["Booking/SampleTests/Parameterised_Scenario"];
            Assert.That(variants.Status, Is.EqualTo(Status.Fail));
            Assert.That(variants.Error, Does.Contain("THB").And.Contain("total missing"));
            Assert.That(manifest.Summary, Is.EqualTo(new SuiteSummary(2, 1, 1, 0)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void ScenarioWithoutResult_IsNotRun()
    {
        var manifest = SuiteManifest.Build(SampleFeatures(), new Dictionary<string, List<RunResult>>());

        Assert.That(manifest.Summary, Is.EqualTo(new SuiteSummary(2, 0, 0, 2)));
    }

    [Test]
    public void Diff_ReportsAddedRemovedStatusAndStepChanges()
    {
        static ScenarioManifest S(string id, string status, string label = "a") =>
            new(id, id, false, null, status, null, null, [new StepManifest("Then", label)]);
        static SuiteManifest M(params ScenarioManifest[] s) =>
            new(1, new SuiteSummary(s.Length, 0, 0, 0), [new FeatureManifest("F", "F", s)]);

        var diff = SuiteDiff.Compute(
            M(S("F/kept", Status.Pass), S("F/gone", Status.Pass), S("F/broke", Status.Pass), S("F/reworded", Status.Pass)),
            M(S("F/kept", Status.Pass), S("F/new", Status.Pass), S("F/broke", Status.Fail), S("F/reworded", Status.Pass, "b")));

        Assert.That(diff.Changes.Select(c => (c.Id, c.Kind)), Is.EquivalentTo(new[]
        {
            ("F/new", SuiteDiff.Added),
            ("F/gone", SuiteDiff.Removed),
            ("F/broke", SuiteDiff.StatusChanged),
            ("F/reworded", SuiteDiff.StepsChanged),
        }));
    }

    [Test]
    public async Task Baseline_IsIgnoredWhenMissingOrFromAnotherSchema()
    {
        var file = Path.GetTempFileName();
        try
        {
            Assert.That(await SuiteManifest.TryLoadAsync(file + ".missing"), Is.Null);
            await File.WriteAllTextAsync(file, "not json");
            Assert.That(await SuiteManifest.TryLoadAsync(file), Is.Null);
            await File.WriteAllTextAsync(file, "{ \"schemaVersion\": 99, \"summary\": {}, \"features\": [] }");
            Assert.That(await SuiteManifest.TryLoadAsync(file), Is.Null);
        }
        finally
        {
            File.Delete(file);
        }
    }
}
