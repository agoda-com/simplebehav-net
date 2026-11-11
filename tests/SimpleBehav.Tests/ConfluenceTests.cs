using System.Net;
using System.Text;
using System.Xml.Linq;
using SimpleBehav.SpecGen;

namespace SimpleBehav.Tests;

public class ConfluenceTests
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
                    .Then("outcome one", See.TextVisible("x"));
            }

            [TestCase("USD")]
            [TestCase("THB")]
            public async Task Parameterised_Scenario(string currency)
            {
                await Scenario().Given(Pages.Checkout).When("acts", p => p.Go()).Then("done", See.TextVisible("z"));
            }
        }
        """;

    private static IReadOnlyList<Feature> Features() =>
    [
        new Feature("SampleTests", "Booking", SpecParser.ParseTestSource(Source).Where(s => s.Steps.Count > 0).ToList()),
        new Feature("OtherTests", "", SpecParser.ParseTestSource(Source).Where(s => s.Steps.Count > 0).ToList()),
    ];

    private static ConfluenceOptions Options(string scope = "main", string space = "SB", string? reports = "https://pages.example/reports",
        bool prune = false, string name = "SimpleBehav") =>
        new("https://wiki.example/wiki", "bot@example.com", "s3cr3t-token", space, name, scope, reports, null, prune);

    private static IReadOnlyList<ConfluencePage> Render(ConfluenceOptions o, SuiteDiff? diff = null,
        IReadOnlyDictionary<string, List<RunResult>>? results = null)
    {
        var f = Features();
        return ConfluenceRenderer.Render(f, SuiteManifest.Build(f, results), diff, o);
    }

    // ---- in-memory Confluence ----

    private sealed class FakeConfluence : IConfluenceClient
    {
        public List<ConfluenceSpace> Spaces { get; } = [new("SB", "SimpleBehav", "global")];
        public Dictionary<string, (RemotePage Page, string Body, string Space)> Pages { get; } = new();
        public List<string> Writes { get; } = [];
        public string? FailOnCreateTitleContaining { get; set; }
        private int _id = 100;

        public Task<ConfluenceSpace?> GetSpaceAsync(string key, CancellationToken ct) =>
            Task.FromResult(Spaces.FirstOrDefault(s => s.Key == key));

        public Task<IReadOnlyList<ConfluenceSpace>> ListSpacesAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ConfluenceSpace>>(Spaces);

        public Task<RemotePage?> FindPageAsync(string spaceKey, string title, CancellationToken ct) =>
            Task.FromResult(Pages.Values.Where(p => p.Space == spaceKey && p.Page.Title == title).Select(p => p.Page).FirstOrDefault());

        public Task<IReadOnlyList<RemotePage>> FindPagesByLabelAsync(string spaceKey, string label, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<RemotePage>>(Pages.Values
                .Where(p => p.Space == spaceKey && p.Page.Labels.Contains(label)).Select(p => p.Page).ToList());

        public Task<RemotePage> CreatePageAsync(string spaceKey, string title, string body, string? parentId, CancellationToken ct)
        {
            if (FailOnCreateTitleContaining is { } f && title.Contains(f))
                throw new ConfluenceException("POST failed: 500", HttpStatusCode.InternalServerError);
            var page = new RemotePage((++_id).ToString(), title, 1, new HashSet<string>(), "https://wiki.example/p/" + _id);
            Pages[page.Id] = (page, body, spaceKey);
            Writes.Add("create " + title);
            ParentOf[page.Id] = parentId;
            return Task.FromResult(page);
        }

        public Dictionary<string, string?> ParentOf { get; } = new();

        public Task<RemotePage> UpdatePageAsync(RemotePage existing, string body, CancellationToken ct)
        {
            var cur = Pages[existing.Id];
            var page = cur.Page with { Version = cur.Page.Version + 1 };
            Pages[existing.Id] = (page, body, cur.Space);
            Writes.Add("update " + existing.Title);
            return Task.FromResult(page);
        }

        public Task AddLabelsAsync(string pageId, IEnumerable<string> labels, CancellationToken ct)
        {
            var cur = Pages[pageId];
            Pages[pageId] = (cur.Page with { Labels = cur.Page.Labels.Concat(labels).ToHashSet() }, cur.Body, cur.Space);
            return Task.CompletedTask;
        }

        public Task TrashPageAsync(string pageId, CancellationToken ct)
        {
            Pages.Remove(pageId);
            Writes.Add("trash " + pageId);
            return Task.CompletedTask;
        }

        public void AddForeignPage(string title, string space = "SB")
        {
            var page = new RemotePage((++_id).ToString(), title, 3, new HashSet<string>(), "https://wiki.example/p/" + _id);
            Pages[page.Id] = (page, "hand written", space);
        }
    }

    private static Task<PublishResult> Publish(FakeConfluence c, ConfluenceOptions o, IReadOnlyList<ConfluencePage>? pages = null) =>
        new ConfluencePublisher(c, o).PublishAsync(pages ?? Render(o));

    // ---- space selection ----

    [Test]
    public async Task Space_ResolvedByKey()
    {
        var result = await Publish(new FakeConfluence(), Options(space: "SB"));
        Assert.That(result.Space.Key, Is.EqualTo("SB"));
    }

    [Test]
    public async Task Space_FallsBackToUniqueExactName_AndPublishesToItsRealKey()
    {
        var c = new FakeConfluence();
        c.Spaces.Add(new ConfluenceSpace("OTHER", "Other", "global"));
        var result = await Publish(c, Options(space: "simplebehav"));

        Assert.That(result.Space.Key, Is.EqualTo("SB"));
        Assert.That(c.Pages.Values.Select(p => p.Space).Distinct(), Is.EqualTo(new[] { "SB" }));
    }

    [Test]
    public void Space_Unresolvable_FailsWithoutWriting()
    {
        var c = new FakeConfluence();
        var ex = Assert.ThrowsAsync<ConfluencePublishException>(() => Publish(c, Options(space: "NOPE")));
        Assert.That(ex!.Message, Does.Contain("NOPE").And.Contain("nothing was published"));
        Assert.That(c.Writes, Is.Empty);
    }

    [Test]
    public void Space_AmbiguousName_Fails()
    {
        var c = new FakeConfluence();
        c.Spaces.Add(new ConfluenceSpace("SB2", "SimpleBehav", "global"));
        var ex = Assert.ThrowsAsync<ConfluencePublishException>(() => Publish(c, Options(space: "SimpleBehav")));
        Assert.That(ex!.Message, Does.Contain("ambiguous").And.Contain("SB").And.Contain("SB2"));
        Assert.That(c.Writes, Is.Empty);
    }

    [Test]
    public void Space_Personal_IsRejected()
    {
        var c = new FakeConfluence();
        c.Spaces.Add(new ConfluenceSpace("~me", "Me", "personal"));
        Assert.ThrowsAsync<ConfluencePublishException>(() => Publish(c, Options(space: "~me")));
        Assert.That(c.Writes, Is.Empty);
    }

    [Test]
    public void Options_DefaultSpaceIsSimpleBehav_AndSecretsComeFromEnvironmentOnly()
    {
        var env = new Dictionary<string, string?>
        {
            ["CONFLUENCE_BASE_URL"] = "https://wiki.example/wiki/", ["CONFLUENCE_USER"] = "u", ["CONFLUENCE_TOKEN"] = "t",
        };
        var o = ConfluenceOptions.FromEnvironment(_ => null, k => env.GetValueOrDefault(k), false);
        Assert.That(o.Space, Is.EqualTo("SimpleBehav"));
        Assert.That(o.Scope, Is.EqualTo("main"));
        Assert.That(o.BaseUrl, Is.EqualTo("https://wiki.example/wiki"));

        var overridden = ConfluenceOptions.FromEnvironment(n => n == "--confluence-space" ? "ABC" : null, k => env.GetValueOrDefault(k), false);
        Assert.That(overridden.Space, Is.EqualTo("ABC"));
    }

    [TestCase("CONFLUENCE_TOKEN")]
    [TestCase("CONFLUENCE_USER")]
    [TestCase("CONFLUENCE_BASE_URL")]
    public void Options_MissingCredential_Fails(string missing)
    {
        var env = new Dictionary<string, string?>
        {
            ["CONFLUENCE_BASE_URL"] = "https://wiki.example/wiki", ["CONFLUENCE_USER"] = "u", ["CONFLUENCE_TOKEN"] = "t",
        };
        env.Remove(missing);
        var ex = Assert.Throws<ConfluenceConfigException>(() =>
            ConfluenceOptions.FromEnvironment(_ => null, k => env.GetValueOrDefault(k), false));
        Assert.That(ex!.Message, Does.Contain(missing));
    }

    [TestCase("Main")]
    [TestCase("mr 1")]
    [TestCase("")]
    public void Options_InvalidScope_Fails(string scope) =>
        Assert.Throws<ConfluenceConfigException>(() => ConfluenceOptions.ForRendering(null, scope, null));

    // ---- create / update ----

    [Test]
    public async Task FirstPublish_CreatesIndexGraphAndFeaturePages_UnderTheRoot_WithOwnerLabels()
    {
        var c = new FakeConfluence();
        var o = Options();
        var result = await Publish(c, o);

        Assert.That(result.Pages, Has.Count.EqualTo(4)); // index + graph + 2 features
        Assert.That(result.Pages.All(p => p.Created));
        var rootId = result.Pages.Single(p => p.Key == ConfluenceRenderer.IndexKey).Id;
        foreach (var p in result.Pages.Where(p => p.Key != ConfluenceRenderer.IndexKey))
            Assert.That(c.ParentOf[p.Id], Is.EqualTo(rootId));
        Assert.That(c.Pages.Values.All(p => p.Page.Labels.Contains(o.OwnerLabel) && p.Page.Labels.Contains(ConfluenceOptions.ManagedLabel)));
        Assert.That(c.Writes.First(), Does.StartWith("create SimpleBehav Living Documentation"));
    }

    [Test]
    public async Task RepeatedPublish_UpdatesInPlace_WithoutDuplicates()
    {
        var c = new FakeConfluence();
        var o = Options();
        var first = await Publish(c, o);
        var second = await Publish(c, o);

        Assert.That(c.Pages, Has.Count.EqualTo(4));
        Assert.That(second.Pages.All(p => !p.Created));
        Assert.That(second.Pages.Select(p => p.Id), Is.EquivalentTo(first.Pages.Select(p => p.Id)));
        Assert.That(c.Pages.Values.All(p => p.Page.Version == 2));
        Assert.That(second.Stale, Is.Empty);
    }

    [Test]
    public async Task RemovedFeature_IsReportedStale_AndOnlyTrashedWithPrune()
    {
        var c = new FakeConfluence();
        await Publish(c, Options());
        var fewer = Render(Options()).Where(p => p.Key != "feature:OtherTests").ToList();

        var kept = await Publish(c, Options(), fewer);
        Assert.That(kept.Stale, Has.Count.EqualTo(1));
        Assert.That(c.Pages, Has.Count.EqualTo(4));

        await Publish(c, Options(prune: true), fewer);
        Assert.That(c.Pages, Has.Count.EqualTo(3));
    }

    // ---- isolation ----

    [Test]
    public async Task MrPreview_UsesSeparatePages_AndLeavesTheDefaultBranchUntouched()
    {
        var c = new FakeConfluence();
        var main = await Publish(c, Options());
        var mainVersions = c.Pages.ToDictionary(p => p.Key, p => p.Value.Page.Version);

        var preview = await Publish(c, Options(scope: "mr-12"));

        Assert.That(preview.Pages.All(p => p.Created));
        Assert.That(preview.Pages.Select(p => p.Title).Intersect(main.Pages.Select(p => p.Title)), Is.Empty);
        Assert.That(preview.Pages.First(p => p.Key == ConfluenceRenderer.IndexKey).Title, Does.Contain("preview mr-12"));
        foreach (var (id, version) in mainVersions)
            Assert.That(c.Pages[id].Page.Version, Is.EqualTo(version));
        Assert.That(c.Pages, Has.Count.EqualTo(8));
    }

    [Test]
    public async Task MrPreview_PruneNeverTouchesOtherScopes()
    {
        var c = new FakeConfluence();
        await Publish(c, Options());
        await Publish(c, Options(scope: "mr-1"));
        var fewer = Render(Options(scope: "mr-1")).Where(p => p.Key != "feature:OtherTests").ToList();

        await Publish(c, Options(scope: "mr-1", prune: true), fewer);

        Assert.That(c.Pages.Values.Count(p => p.Page.Labels.Contains(Options().OwnerLabel)), Is.EqualTo(4));
        Assert.That(c.Pages.Values.Count(p => p.Page.Labels.Contains(Options(scope: "mr-1").OwnerLabel)), Is.EqualTo(3));
    }

    [Test]
    public async Task DifferentProjectsCanShareASpace()
    {
        var c = new FakeConfluence();
        await Publish(c, Options(name: "ProjectA"));
        await Publish(c, Options(name: "ProjectB"));
        Assert.That(c.Pages, Has.Count.EqualTo(8));
    }

    [Test]
    public void UnrelatedPageWithSameTitle_IsNeverOverwritten_AndNothingIsWritten()
    {
        var c = new FakeConfluence();
        c.AddForeignPage("SimpleBehav Living Documentation - Page graph");
        var before = c.Pages.Values.Single();

        var ex = Assert.ThrowsAsync<ConfluencePublishException>(() => Publish(c, Options()));

        Assert.That(ex!.Message, Does.Contain("Page graph").And.Contain("does not own"));
        Assert.That(c.Writes, Is.Empty);
        Assert.That(c.Pages.Values.Single(), Is.EqualTo(before));
    }

    [Test]
    public async Task ForeignPageInAnotherSpace_IsIgnored()
    {
        var c = new FakeConfluence();
        c.AddForeignPage("SimpleBehav Living Documentation", space: "ELSEWHERE");
        await Publish(c, Options());
        Assert.That(c.Pages.Values.Count(p => p.Space == "ELSEWHERE"), Is.EqualTo(1));
        Assert.That(c.Pages.Values.First(p => p.Space == "ELSEWHERE").Body, Is.EqualTo("hand written"));
    }

    // ---- rendering and links ----

    private static XElement Parse(string body) => XElement.Parse(
        $"<root xmlns:ac=\"http://atlassian.com/content\" xmlns:ri=\"http://atlassian.com/resource/identifier\">{body}</root>");

    [Test]
    public void Pages_AreWellFormedStorageFormat_WithUniqueTitles()
    {
        var pages = Render(Options());
        foreach (var p in pages) Assert.DoesNotThrow(() => Parse(p.Body), p.Title);
        Assert.That(pages.Select(p => p.Title).Distinct().Count(), Is.EqualTo(pages.Count));
        Assert.That(pages.All(p => p.Title.IndexOfAny([':', '@', '/', '\\', '|', '^']) < 0));
    }

    [Test]
    public void InternalLinks_AllPointAtPagesInThePublishedSet()
    {
        var pages = Render(Options(), new SuiteDiff([new ScenarioChange("Booking/SampleTests/Plain_Scenario", "Plain Scenario", SuiteDiff.Added)]));
        var titles = pages.Select(p => p.Title).ToHashSet();
        XNamespace ri = "http://atlassian.com/resource/identifier";

        foreach (var p in pages)
        {
            var targets = Parse(p.Body).Descendants(ri + "page").Select(e => (string)e.Attribute(ri + "content-title")!).Distinct().ToList();
            Assert.That(targets, Is.Not.Empty, p.Title);
            Assert.That(targets, Is.SubsetOf(titles), p.Title);
        }
    }

    [Test]
    public void ScenarioAnchors_ExistOnFeaturePages_AndAreLinkedFromIndexAndGraph()
    {
        var pages = Render(Options()).ToDictionary(p => p.Key);
        Assert.That(pages["feature:Booking/SampleTests"].Body, Does.Contain("<ac:parameter ac:name=\"\">Plain_Scenario</ac:parameter>"));
        Assert.That(pages[ConfluenceRenderer.IndexKey].Body, Does.Contain("ac:anchor=\"Plain_Scenario\""));
        Assert.That(pages[ConfluenceRenderer.GraphKey].Body, Does.Contain("ac:anchor=\"Parameterised_Scenario\""));
    }

    [Test]
    public void RunReportAndTraceLinks_PointAtTheReportsUrl_OrAreOmitted()
    {
        var withReports = Render(Options(reports: "https://pages.example/mr/5/Tests/")).ToDictionary(p => p.Key);
        Assert.That(withReports["feature:Booking/SampleTests"].Body,
            Does.Contain("href=\"https://pages.example/mr/5/Tests/Booking/SampleTests/\""));
        Assert.That(withReports[ConfluenceRenderer.IndexKey].Body, Does.Contain("href=\"https://pages.example/mr/5/Tests/Booking/SampleTests/\""));

        var without = Render(Options(reports: null));
        Assert.That(without.Any(p => p.Body.Contains("href=")), Is.False);
    }

    [Test]
    public void ReportsUrl_MustBeHttp()
    {
        Assert.Throws<ConfluenceConfigException>(() => ConfluenceOptions.ForRendering(null, null, "javascript:alert(1)"));
    }

    [Test]
    public void Content_PreservesStepsCasesResultsErrorsAndBaselineChanges()
    {
        var results = new Dictionary<string, List<RunResult>>
        {
            ["Booking/SampleTests"] =
            [
                new("Plain_Scenario", "pass", 12, null, "plain"),
                new("Parameterised_Scenario[THB]", "fail", 5, "total ]]> missing", "thb"),
            ],
        };
        var diff = new SuiteDiff([
            new ScenarioChange("Booking/SampleTests/Plain_Scenario", "Plain Scenario", SuiteDiff.StatusChanged, Status.Fail, Status.Pass),
            new ScenarioChange("Gone/Old/Thing", "Thing", SuiteDiff.Removed),
        ]);
        var pages = Render(Options(), diff, results).ToDictionary(p => p.Key);

        var feature = pages["feature:Booking/SampleTests"].Body;
        Assert.That(feature, Does.Contain("<th>Given</th><td><code>Pages.Checkout</code></td>")
            .And.Contain("user acts").And.Contain("outcome one")
            .And.Contain("<code>USD</code>").And.Contain("<code>THB</code>")
            .And.Contain("PASS").And.Contain("FAIL").And.Contain("12ms")
            .And.Contain("total ]]]]><![CDATA[> missing"));
        Assert.DoesNotThrow(() => Parse(feature));

        var index = pages[ConfluenceRenderer.IndexKey].Body;
        Assert.That(index, Does.Contain("Changes since baseline").And.Contain("NOW PASSING").And.Contain("REMOVED"));
        Assert.That(index, Does.Contain("1 passed").And.Contain("1 failed"));

        var graph = pages[ConfluenceRenderer.GraphKey].Body;
        Assert.That(graph, Does.Contain("CheckoutPage").And.Contain("Pages.Checkout").And.Contain("outcome one"));
    }

    // ---- failure handling ----

    [Test]
    public async Task ServerError_Midway_Throws_AndRerunConvergesWithoutDuplicates()
    {
        var c = new FakeConfluence { FailOnCreateTitleContaining = "Other" };
        Assert.ThrowsAsync<ConfluenceException>(() => Publish(c, Options()));
        Assert.That(c.Pages.Count, Is.LessThan(4));

        c.FailOnCreateTitleContaining = null;
        await Publish(c, Options());
        Assert.That(c.Pages, Has.Count.EqualTo(4));
    }

    // ---- HTTP client ----

    private sealed class StubHandler(Func<HttpRequestMessage, string?, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Url, string? Body)> Calls { get; } = [];
        public HttpRequestMessage? Last { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            Calls.Add((request.Method, request.RequestUri!.ToString(), body));
            Last = request;
            return respond(request, body);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode code, string json) =>
        new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static HttpConfluenceClient Client(StubHandler h, ConfluenceOptions? o = null) =>
        new(new HttpClient(h), o ?? Options(), (_, _) => Task.CompletedTask);

    [Test]
    public async Task Http_SendsBasicAuth_AndMapsMissingSpaceToNull()
    {
        var h = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.NotFound));
        var space = await Client(h).GetSpaceAsync("SB", default);

        Assert.That(space, Is.Null);
        Assert.That(h.Calls.Single().Url, Is.EqualTo("https://wiki.example/wiki/rest/api/space/SB"));
        var expected = Convert.ToBase64String(Encoding.UTF8.GetBytes("bot@example.com:s3cr3t-token"));
        Assert.That(h.Last!.Headers.Authorization!.Parameter, Is.EqualTo(expected));
    }

    [Test]
    public void Http_AuthFailure_IsAnError_NotAMissingSpace_AndNeverEchoesTheToken()
    {
        var h = new StubHandler((_, _) => Json(HttpStatusCode.Unauthorized, "{\"message\":\"bad token s3cr3t-token\"}"));
        var ex = Assert.ThrowsAsync<ConfluenceException>(() => Client(h).GetSpaceAsync("SB", default));

        Assert.That(ex!.Status, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(ex.Message, Does.Contain("401").And.Contain("CONFLUENCE_TOKEN"));
        Assert.That(ex.Message, Does.Not.Contain("s3cr3t-token"));
    }

    [Test]
    public async Task Http_CreateAndUpdate_SendStorageBody_ParentAndNextVersion()
    {
        var h = new StubHandler((req, _) => Json(HttpStatusCode.OK,
            "{\"id\":\"9\",\"title\":\"T\",\"version\":{\"number\":4},\"metadata\":{\"labels\":{\"results\":[{\"name\":\"x\"}]}},\"_links\":{\"webui\":\"/spaces/SB/pages/9\"}}"));
        var c = Client(h);

        var created = await c.CreatePageAsync("SB", "T", "<p>hi</p>", "55", default);
        Assert.That(created.Url, Is.EqualTo("https://wiki.example/wiki/spaces/SB/pages/9"));
        Assert.That(created.Labels, Is.EquivalentTo(new[] { "x" }));
        var create = System.Text.Json.Nodes.JsonNode.Parse(h.Calls[0].Body!)!;
        Assert.That((string)create["space"]!["key"]!, Is.EqualTo("SB"));
        Assert.That((string)create["ancestors"]![0]!["id"]!, Is.EqualTo("55"));
        Assert.That((string)create["body"]!["storage"]!["representation"]!, Is.EqualTo("storage"));

        await c.UpdatePageAsync(created, "<p>v2</p>", default);
        Assert.That(h.Calls[1].Method, Is.EqualTo(HttpMethod.Put));
        Assert.That((int)System.Text.Json.Nodes.JsonNode.Parse(h.Calls[1].Body!)!["version"]!["number"]!, Is.EqualTo(5));
    }

    [Test]
    public async Task Http_RetriesRateLimit_ThenGivesUp()
    {
        var calls = 0;
        var h = new StubHandler((_, _) => ++calls < 3
            ? new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            : Json(HttpStatusCode.OK, "{\"key\":\"SB\",\"name\":\"n\",\"type\":\"global\"}"));
        Assert.That((await Client(h).GetSpaceAsync("SB", default))!.Key, Is.EqualTo("SB"));
        Assert.That(calls, Is.EqualTo(3));

        var always = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        Assert.ThrowsAsync<ConfluenceException>(() => Client(always).GetSpaceAsync("SB", default));
        Assert.That(always.Calls, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task Http_Publish_EndToEnd_NeverLogsTheToken()
    {
        var logs = new List<string>();
        var h = new StubHandler((req, _) =>
        {
            var url = req.RequestUri!.AbsolutePath;
            if (url.EndsWith("/space/SB")) return Json(HttpStatusCode.OK, "{\"key\":\"SB\",\"name\":\"SimpleBehav\",\"type\":\"global\"}");
            if (req.Method == HttpMethod.Get && url.EndsWith("/content")) return Json(HttpStatusCode.OK, "{\"results\":[]}");
            if (url.EndsWith("/content/search")) return Json(HttpStatusCode.OK, "{\"results\":[]}");
            if (url.EndsWith("/label")) return Json(HttpStatusCode.OK, "[]");
            return Json(HttpStatusCode.OK, "{\"id\":\"1\",\"title\":\"t\",\"version\":{\"number\":1},\"_links\":{\"webui\":\"/x\"}}");
        });
        var o = Options();
        await new ConfluencePublisher(Client(h, o), o, logs.Add).PublishAsync(Render(o));

        Assert.That(logs, Is.Not.Empty);
        Assert.That(string.Join("\n", logs), Does.Not.Contain("s3cr3t-token"));
        Assert.That(h.Calls.Any(c => c.Method == HttpMethod.Delete), Is.False);
    }

    [Test]
    public async Task DryRun_RendersPagesToDisk_WithoutCredentialsOrNetwork()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var tests = Path.Combine(root, "tests", "Booking");
        Directory.CreateDirectory(tests);
        await File.WriteAllTextAsync(Path.Combine(tests, "SampleTests.cs"), Source);
        try
        {
            var o = ConfluenceOptions.ForRendering(null, "mr-3", "https://pages.example/r");
            var features = await LivingDocEmitter.LoadFeaturesAsync(Path.Combine(root, "tests"));
            var pages = ConfluenceRenderer.Render(features, SuiteManifest.Build(features, null), null, o);

            Assert.That(pages[0].Title, Is.EqualTo("SimpleBehav Living Documentation (preview mr-3)"));
            Assert.That(o.Token, Is.Empty);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
