using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SimpleBehav.SpecGen;

public sealed record ConfluenceSpace(string Key, string Name, string Type);

public sealed record RemotePage(string Id, string Title, int Version, IReadOnlySet<string> Labels, string Url);

public sealed class ConfluenceException(string message, HttpStatusCode? status = null) : Exception(message)
{
    public HttpStatusCode? Status { get; } = status;
}

/// <summary>The few Confluence operations publishing needs, so the publisher can be tested without a server.</summary>
public interface IConfluenceClient
{
    /// <summary>Null only when the space doesn't exist; auth and server errors throw.</summary>
    Task<ConfluenceSpace?> GetSpaceAsync(string key, CancellationToken ct);
    Task<IReadOnlyList<ConfluenceSpace>> ListSpacesAsync(CancellationToken ct);
    Task<RemotePage?> FindPageAsync(string spaceKey, string title, CancellationToken ct);
    Task<IReadOnlyList<RemotePage>> FindPagesByLabelAsync(string spaceKey, string label, CancellationToken ct);
    Task<RemotePage> CreatePageAsync(string spaceKey, string title, string body, string? parentId, CancellationToken ct);
    Task<RemotePage> UpdatePageAsync(RemotePage existing, string body, CancellationToken ct);
    Task AddLabelsAsync(string pageId, IEnumerable<string> labels, CancellationToken ct);
    /// <summary>Moves the page to the Confluence trash (recoverable).</summary>
    Task TrashPageAsync(string pageId, CancellationToken ct);
}

/// <summary>Confluence Cloud REST API v1 (<c>/wiki/rest/api</c>) with basic auth (user + API token).</summary>
public sealed class HttpConfluenceClient : IConfluenceClient
{
    private const int MaxAttempts = 3;
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _token;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public HttpConfluenceClient(HttpClient http, ConfluenceOptions options, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _http = http;
        _baseUrl = options.BaseUrl.TrimEnd('/');
        _token = options.Token;
        _delay = delay ?? Task.Delay;
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.User}:{options.Token}")));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<ConfluenceSpace?> GetSpaceAsync(string key, CancellationToken ct)
    {
        var (status, json) = await SendAsync(HttpMethod.Get, $"/rest/api/space/{Uri.EscapeDataString(key)}", null, ct, allow404: true);
        return status == HttpStatusCode.NotFound ? null : ToSpace(json!);
    }

    public async Task<IReadOnlyList<ConfluenceSpace>> ListSpacesAsync(CancellationToken ct)
    {
        var spaces = new List<ConfluenceSpace>();
        for (var start = 0; ; )
        {
            var (_, json) = await SendAsync(HttpMethod.Get, $"/rest/api/space?limit=100&start={start}", null, ct);
            var results = json!["results"]!.AsArray();
            spaces.AddRange(results.Select(r => ToSpace(r!)));
            if (results.Count < 100) return spaces;
            start += results.Count;
        }
    }

    public async Task<RemotePage?> FindPageAsync(string spaceKey, string title, CancellationToken ct)
    {
        var (_, json) = await SendAsync(HttpMethod.Get,
            $"/rest/api/content?type=page&spaceKey={Uri.EscapeDataString(spaceKey)}&title={Uri.EscapeDataString(title)}&expand=version,metadata.labels",
            null, ct);
        var first = json!["results"]!.AsArray().FirstOrDefault();
        return first is null ? null : ToPage(first);
    }

    public async Task<IReadOnlyList<RemotePage>> FindPagesByLabelAsync(string spaceKey, string label, CancellationToken ct)
    {
        var cql = Uri.EscapeDataString($"type = page AND space = \"{spaceKey}\" AND label = \"{label}\"");
        var pages = new List<RemotePage>();
        for (var start = 0; ; )
        {
            var (_, json) = await SendAsync(HttpMethod.Get,
                $"/rest/api/content/search?cql={cql}&limit=100&start={start}&expand=version,metadata.labels", null, ct);
            var results = json!["results"]!.AsArray();
            pages.AddRange(results.Select(r => ToPage(r!)));
            if (results.Count < 100) return pages;
            start += results.Count;
        }
    }

    public async Task<RemotePage> CreatePageAsync(string spaceKey, string title, string body, string? parentId, CancellationToken ct)
    {
        var payload = new JsonObject
        {
            ["type"] = "page",
            ["title"] = title,
            ["space"] = new JsonObject { ["key"] = spaceKey },
            ["body"] = Storage(body),
        };
        if (parentId is not null) payload["ancestors"] = new JsonArray(new JsonObject { ["id"] = parentId });
        var (_, json) = await SendAsync(HttpMethod.Post, "/rest/api/content?expand=version,metadata.labels", payload, ct);
        return ToPage(json!);
    }

    public async Task<RemotePage> UpdatePageAsync(RemotePage existing, string body, CancellationToken ct)
    {
        var payload = new JsonObject
        {
            ["type"] = "page",
            ["title"] = existing.Title,
            ["version"] = new JsonObject { ["number"] = existing.Version + 1 },
            ["body"] = Storage(body),
        };
        var (_, json) = await SendAsync(HttpMethod.Put, $"/rest/api/content/{existing.Id}?expand=version,metadata.labels", payload, ct);
        return ToPage(json!);
    }

    public async Task AddLabelsAsync(string pageId, IEnumerable<string> labels, CancellationToken ct)
    {
        var payload = new JsonArray(labels.Select(l => (JsonNode)new JsonObject { ["prefix"] = "global", ["name"] = l }).ToArray());
        await SendAsync(HttpMethod.Post, $"/rest/api/content/{pageId}/label", payload, ct);
    }

    public async Task TrashPageAsync(string pageId, CancellationToken ct) =>
        await SendAsync(HttpMethod.Delete, $"/rest/api/content/{pageId}", null, ct);

    private static JsonObject Storage(string body) =>
        new() { ["storage"] = new JsonObject { ["value"] = body, ["representation"] = "storage" } };

    private static ConfluenceSpace ToSpace(JsonNode n) =>
        new((string)n["key"]!, (string?)n["name"] ?? "", (string?)n["type"] ?? "");

    private RemotePage ToPage(JsonNode n)
    {
        var labels = n["metadata"]?["labels"]?["results"]?.AsArray().Select(l => (string)l!["name"]!).ToHashSet()
                     ?? new HashSet<string>();
        var webui = (string?)n["_links"]?["webui"];
        return new RemotePage((string)n["id"]!, (string)n["title"]!, (int?)n["version"]?["number"] ?? 1, labels,
            webui is null ? "" : _baseUrl + webui);
    }

    private async Task<(HttpStatusCode Status, JsonNode? Json)> SendAsync(
        HttpMethod method, string path, JsonNode? payload, CancellationToken ct, bool allow404 = false)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var request = new HttpRequestMessage(method, _baseUrl + path);
            if (payload is not null)
                request.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, ct);
            }
            catch (HttpRequestException e)
            {
                throw new ConfluenceException($"{method} {path}: {Scrub(e.Message)}");
            }

            using (response)
            {
                var text = await response.Content.ReadAsStringAsync(ct);
                if (response.IsSuccessStatusCode)
                    return (response.StatusCode, text.Length == 0 ? null : JsonNode.Parse(text));
                if (allow404 && response.StatusCode == HttpStatusCode.NotFound)
                    return (response.StatusCode, null);

                if (attempt < MaxAttempts && response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
                {
                    var wait = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(attempt * 2);
                    await _delay(wait > TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : wait, ct);
                    continue;
                }

                var hint = response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                    ? " (check CONFLUENCE_USER / CONFLUENCE_TOKEN and the account's permission on the space)" : "";
                throw new ConfluenceException(
                    $"{method} {path} failed: {(int)response.StatusCode} {response.ReasonPhrase}{hint}. {Scrub(Truncate(text))}",
                    response.StatusCode);
            }
        }
    }

    private string Scrub(string s) => _token.Length == 0 ? s : s.Replace(_token, "***");

    private static string Truncate(string s) => s.Length <= 300 ? s : s[..300] + "…";
}
