using SimpleBehav;
using SimpleBehav.Reporter;

namespace SimpleBehav.Tests;

public class ArtifactTests
{
    [Test]
    public async Task EmitAsync_LongShardTitles_DoNotCollapseToTheSameDirectory()
    {
        var method = "UserFilledAllPropertyDetailsUpdatesThemProceedToTheNextStepAndValidatePayload";
        var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var suite = new[] { "SimpleBehav.Tests", "OneOnboarding", "DetailsPageFeatures" };

        var mobile = await new ArtifactEmitter($"{method}[chromium-mobile {method}]")
            .EmitAsync(new ArtifactEmitter($"{method}[chromium-mobile {method}]").Build("pass"), tmp, suite);
        var desktop = await new ArtifactEmitter($"{method}[chromium-desktop {method}]")
            .EmitAsync(new ArtifactEmitter($"{method}[chromium-desktop {method}]").Build("pass"), tmp, suite);

        Assert.That(mobile, Is.Not.EqualTo(desktop));
        Assert.That(Directory.Exists(mobile));
        Assert.That(Directory.Exists(desktop));
        Directory.Delete(tmp, recursive: true);
    }

    [Test]
    public async Task RebuildAllAsync_RewritesSuiteIndex_FromEveryShardBundle()
    {
        var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var suitePath = new[] { "SimpleBehav.Tests", "Booking", "CheckoutTests" };
        var suiteDir = Path.Combine(tmp, Path.Combine(suitePath));

        await WriteShard(tmp, suitePath, "PlaceOrder[chromium-mobile PlaceOrder]", "pass");
        await WriteShard(tmp, suitePath, "PlaceOrder[chromium-desktop PlaceOrder]", "pass");
        // Stale per-shard index that only lists mobile (simulates GitLab last-writer-wins).
        await File.WriteAllTextAsync(Path.Combine(suiteDir, "index.html"),
            "<html><body data-title=\"PlaceOrder[chromium-mobile PlaceOrder]\"></body></html>");

        var rebuilt = await SuiteReporter.RebuildAllAsync(tmp);
        var index = await File.ReadAllTextAsync(Path.Combine(suiteDir, "index.html"));

        Assert.That(rebuilt, Is.EqualTo(1));
        Assert.That(index, Does.Contain("chromium-mobile").And.Contain("chromium-desktop"));
        Directory.Delete(tmp, recursive: true);
    }

    private static async Task WriteShard(string root, string[] suitePath, string title, string status)
    {
        var emitter = new ArtifactEmitter(title);
        emitter.AddStep(new ArtifactStep { Type = StepType.Assert, Label = "ok", DurationMs = 1 });
        await emitter.EmitAsync(emitter.Build(status), root, suitePath);
    }

    [Test]
    public void Build_ReturnsBundle_WithCorrectFields()
    {
        var emitter = new ArtifactEmitter("my scenario");
        emitter.AddStep(new ArtifactStep { Type = StepType.Setup, Label = "preconditions applied", DurationMs = 5 });
        emitter.AddStep(new ArtifactStep { Type = StepType.Act, Label = "interaction complete", DurationMs = 120 });
        emitter.AddStep(new ArtifactStep { Type = StepType.Assert, Label = "toast visible", DurationMs = 300 });

        var bundle = emitter.Build("pass");

        Assert.That(bundle.Title, Is.EqualTo("my scenario"));
        Assert.That(bundle.Status, Is.EqualTo("pass"));
        Assert.That(bundle.Steps.Count, Is.EqualTo(3));
        Assert.That(bundle.Id, Is.Not.Empty);
    }

    [Test]
    public async Task EmitAsync_WritesArtifacts_UnderTestSourcePath()
    {
        var emitter = new ArtifactEmitter("emit test");
        emitter.AddStep(new ArtifactStep { Type = StepType.Assert, Label = "report is visible", DurationMs = 12 });
        var bundle = emitter.Build("pass");
        var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        var dir = await emitter.EmitAsync(bundle, tmp, ["SimpleBehav.Tests", "Booking", "CheckoutTests"]);

        Assert.That(dir, Is.EqualTo(Path.Combine(tmp, "SimpleBehav.Tests", "Booking", "CheckoutTests", "emit-test")));
        Assert.That(File.Exists(Path.Combine(dir, "bundle.json")), Is.True);

        var bundleJson = await File.ReadAllTextAsync(Path.Combine(dir, "bundle.json"));
        Assert.Multiple(() =>
        {
            Assert.That(bundleJson, Does.Contain("\"title\": \"emit test\""));
            Assert.That(bundleJson, Does.Contain("\"type\": \"Assert\""));
        });

        var cucumber = await File.ReadAllTextAsync(Path.Combine(dir, "cucumber.json"));
        Assert.Multiple(() =>
        {
            Assert.That(cucumber, Does.Contain("\"keyword\": \"Then \""));
            Assert.That(cucumber, Does.Contain("\"name\": \"CheckoutTests\""));
            Assert.That(cucumber, Does.Contain("\"status\": \"passed\""));
        });

        Directory.Delete(tmp, recursive: true);
    }
}
