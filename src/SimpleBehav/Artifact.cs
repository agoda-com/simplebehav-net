using System.Text.Json;
using System.Text.Json.Serialization;

namespace SimpleBehav;

public enum StepType { Setup, Act, Assert }

public sealed record ArtifactStep
{
    public required StepType Type { get; init; }
    public required string Label { get; init; }
    public required long DurationMs { get; init; }
    public string? Error { get; init; }
}

public sealed record ArtifactBundle
{
    public required string Id { get; init; }
    public required string Title { get; init; }

    public required string Status { get; init; }

    public required string StartedAt { get; init; }
    public required long DurationMs { get; init; }
    public required IReadOnlyList<ArtifactStep> Steps { get; init; }
}

/// <summary>Shared artifact JSON shape — camelCase names and string enums.</summary>
internal static class ArtifactJson
{
    internal static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    internal static string Slug(string value)
    {
        var slug = value.ToLowerInvariant().Replace(' ', '-');
        slug = System.Text.RegularExpressions.Regex.Replace(slug, @"[^a-z0-9\-]", "");
        if (slug.Length <= 80) return slug;

        // Keep a stable unique suffix so long titles (and browser/device shards) do not
        // collide when truncated — otherwise CI artifact merges overwrite each other.
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(value)))[..8].ToLowerInvariant();
        return slug[..71] + "-" + hash;
    }

    /// <summary>
    /// Mirrors the test source tree: &lt;project&gt;/&lt;folders&gt;/&lt;TestFile&gt;.
    /// Everything above the project directory is dropped so published paths stay
    /// independent of the machine that ran the tests.
    /// </summary>
    internal static string[] SuitePath(string projectName, string sourceFile)
    {
        var project = Segment(projectName);
        var fileName = Path.GetFileNameWithoutExtension(sourceFile);
        if (fileName.Length == 0) return [project];

        var segments = sourceFile.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var projectIndex = Array.LastIndexOf(segments, projectName);

        var path = new List<string> { project };
        if (projectIndex >= 0)
        {
            for (var i = projectIndex + 1; i < segments.Length - 1; i++)
                path.Add(Segment(segments[i]));
        }
        path.Add(Segment(fileName));
        return [.. path];
    }

    private static string Segment(string value)
    {
        var segment = System.Text.RegularExpressions.Regex.Replace(value, @"[^A-Za-z0-9._-]", "-");
        return segment.Length == 0 ? "suite" : segment;
    }
}

public sealed class ArtifactEmitter
{
    private readonly string _id = Guid.NewGuid().ToString();
    private readonly long _startedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    private readonly List<ArtifactStep> _steps = [];
    private readonly string _title;

    public ArtifactEmitter(string title) => _title = title;

    public void AddStep(ArtifactStep step) => _steps.Add(step);

    public ArtifactBundle Build(string status) => new()
    {
        Id = _id,
        Title = _title,
        Status = status,
        StartedAt = DateTimeOffset.FromUnixTimeMilliseconds(_startedAt).UtcDateTime.ToString("o"),
        DurationMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - _startedAt,
        Steps = _steps.AsReadOnly(),
    };

    /// <summary>
    /// Writes bundle.json and cucumber.json to
    /// &lt;outDir&gt;/&lt;suite&gt;/&lt;scenario&gt;/ and returns the scenario directory.
    /// </summary>
    public async Task<string> EmitAsync(ArtifactBundle bundle, string outDir, string[] suitePath)
    {
        var slug = ArtifactJson.Slug(bundle.Title);
        var dir = Path.Combine(outDir, Path.Combine(suitePath), slug);
        Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(bundle, ArtifactJson.Options);
        await File.WriteAllTextAsync(Path.Combine(dir, "bundle.json"), json);
        await File.WriteAllTextAsync(Path.Combine(dir, "cucumber.json"), ToCucumberJson(bundle, suitePath, slug));
        return dir;
    }

    private static string ToCucumberJson(ArtifactBundle bundle, string[] suitePath, string slug)
    {
        var steps = bundle.Steps.Select((step, index) => new
        {
            keyword = step.Type switch
            {
                StepType.Setup => "Given ",
                StepType.Act => "When ",
                StepType.Assert => "Then ",
                _ => string.Empty,
            },
            name = step.Label,
            line = index + 1,
            result = new
            {
                duration = step.DurationMs * 1_000_000,
                status = step.Error is null ? "passed" : "failed",
                error_message = step.Error,
            },
        });

        var document = new[]
        {
            new
            {
                uri = string.Join('/', suitePath) + $"/{slug}",
                id = bundle.Id,
                keyword = "Feature",
                name = suitePath[^1],
                line = 1,
                description = "Generated by SimpleBehav",
                elements = new[]
                {
                    new
                    {
                        id = $"{bundle.Id};{slug}",
                        keyword = "Scenario",
                        name = bundle.Title,
                        line = 1,
                        type = "scenario",
                        steps,
                    },
                },
            },
        };

        return JsonSerializer.Serialize(document, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        });
    }
}
