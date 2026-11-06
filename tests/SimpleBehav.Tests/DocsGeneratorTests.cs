using SimpleBehav;
using SimpleBehav.Reporter;

namespace SimpleBehav.Tests;

public class DocsGeneratorTests
{
    private static SuiteScenario Scenario(string title, string status, params ArtifactStep[] steps) =>
        new(title.Replace(' ', '-'), new ArtifactBundle
        {
            Id = Guid.NewGuid().ToString(),
            Title = title,
            Status = status,
            StartedAt = DateTime.UtcNow.ToString("o"),
            DurationMs = 1234,
            Steps = steps,
        });

    [Test]
    public void Generate_ListsEveryScenarioInTheSuite()
    {
        var html = DocsGenerator.Generate("CheckoutTests", [
            Scenario("user places order", "pass",
                new ArtifactStep { Type = StepType.Setup, Label = "checkout page is available", DurationMs = 4 },
                new ArtifactStep { Type = StepType.Assert, Label = "toast visible", DurationMs = 300 }),
            Scenario("user clears card", "pass",
                new ArtifactStep { Type = StepType.Assert, Label = "submit stays disabled", DurationMs = 40 }),
        ], "../../trace-viewer/index.html");

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain("CheckoutTests"));
            Assert.That(html, Does.Contain("user places order"));
            Assert.That(html, Does.Contain("user clears card"));
            Assert.That(html, Does.Contain("Given checkout page is available"));
            Assert.That(html, Does.Contain("Then toast visible"));
            Assert.That(html, Does.Contain("ALL PASSED"));
        });
    }

    [Test]
    public void Generate_PointsTraceViewerAtSuppliedBase()
    {
        var html = DocsGenerator.Generate("CheckoutTests", [
            Scenario("user places order", "pass",
                new ArtifactStep { Type = StepType.Assert, Label = "toast visible", DurationMs = 10 }),
        ], "../../trace-viewer/index.html");

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain("\"../../trace-viewer/index.html\""));
            Assert.That(html, Does.Contain("href=\"#playwright-trace\""));
            Assert.That(html, Does.Not.Contain("data:image/png;base64"));
        });
    }

    [Test]
    public void Generate_FailingScenario_IsSelectedAndTonedDanger()
    {
        var html = DocsGenerator.Generate("CheckoutTests", [
            Scenario("user places order", "pass",
                new ArtifactStep { Type = StepType.Assert, Label = "toast visible", DurationMs = 10 }),
            Scenario("user clears card", "fail",
                new ArtifactStep { Type = StepType.Assert, Label = "FAILED: timeout", DurationMs = 500, Error = "timeout" }),
        ], "../trace-viewer/index.html");

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain("1 FAILED"));
            Assert.That(html, Does.Contain("pre-error"));
            Assert.That(html, Does.Contain("data-title=\"user clears card\" open"));
        });
    }
}
