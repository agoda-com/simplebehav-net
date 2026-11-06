using System.Text;
using System.Text.RegularExpressions;

namespace SimpleBehav.SpecGen;

public sealed class ConfluenceConfigException(string message) : Exception(message);

/// <summary>
/// Where and how living documentation is published to Confluence. Credentials come from the environment only
/// (never from arguments, which end up in process listings and CI logs).
/// </summary>
/// <param name="Space">Confluence space key — or the space's exact name, resolved to a key (and verified) before anything is written.</param>
/// <param name="Name">Product name used in page titles and in the ownership label, so several projects can share one space.</param>
/// <param name="Scope"><c>main</c> for the default branch, <c>mr-&lt;iid&gt;</c> for a merge request preview. Each scope owns separate pages.</param>
/// <param name="ReportsUrl">Absolute URL under which <c>&lt;PagePath&gt;/</c> run reports and traces are hosted; null omits those links.</param>
/// <param name="ParentId">Optional existing page that the living-doc root page is created under.</param>
/// <param name="Prune">Trash owned pages of this scope that are no longer generated.</param>
public sealed record ConfluenceOptions(
    string BaseUrl,
    string User,
    string Token,
    string Space,
    string Name,
    string Scope,
    string? ReportsUrl,
    string? ParentId,
    bool Prune)
{
    public const string DefaultSpace = "SimpleBehav";
    public const string DefaultName = "SimpleBehav";
    public const string DefaultScope = "main";

    /// <summary>Label on every page this tool creates; with <see cref="OwnerLabel"/> it marks a page as safe to overwrite.</summary>
    public const string ManagedLabel = "simplebehav-livingdoc";

    public string OwnerLabel => $"sb-livingdoc-{Slug(Name)}-{Slug(Scope)}";

    public bool IsMain => Scope == DefaultScope;

    public string RootTitle => IsMain ? $"{Name} Living Documentation" : $"{Name} Living Documentation (preview {Scope})";

    /// <summary>Renders only; no credentials needed and nothing is sent anywhere.</summary>
    public static ConfluenceOptions ForRendering(string? name, string? scope, string? reportsUrl) =>
        Validated(new ConfluenceOptions("", "", "", DefaultSpace, name ?? DefaultName, scope ?? DefaultScope, reportsUrl, null, false));

    /// <param name="option">Command-line option lookup (<c>--confluence-space</c> …).</param>
    /// <param name="env">Environment lookup.</param>
    public static ConfluenceOptions FromEnvironment(Func<string, string?> option, Func<string, string?> env, bool prune)
    {
        string? Pick(string optionName, string envName) =>
            option(optionName) is { Length: > 0 } o ? o : env(envName) is { Length: > 0 } e ? e : null;

        string Require(string envName)
        {
            var value = env(envName);
            if (string.IsNullOrWhiteSpace(value))
                throw new ConfluenceConfigException($"{envName} is not set. Provide it as a masked CI/CD variable; it is never read from arguments.");
            return value.Trim();
        }

        return Validated(new ConfluenceOptions(
            Pick("--confluence-url", "CONFLUENCE_BASE_URL") ?? throw new ConfluenceConfigException(
                "CONFLUENCE_BASE_URL is not set (e.g. https://<site>.atlassian.net/wiki)."),
            Require("CONFLUENCE_USER"),
            Require("CONFLUENCE_TOKEN"),
            Pick("--confluence-space", "CONFLUENCE_SPACE") ?? DefaultSpace,
            Pick("--confluence-name", "CONFLUENCE_NAME") ?? DefaultName,
            Pick("--confluence-scope", "CONFLUENCE_SCOPE") ?? DefaultScope,
            Pick("--reports-url", "LIVINGDOC_REPORTS_URL"),
            Pick("--confluence-parent-id", "CONFLUENCE_PARENT_ID"),
            prune));
    }

    private static ConfluenceOptions Validated(ConfluenceOptions o)
    {
        if (o.Space.Length == 0) throw new ConfluenceConfigException("Confluence space must not be empty.");
        if (o.Name.Trim().Length == 0 || Slug(o.Name).Length == 0)
            throw new ConfluenceConfigException("Confluence name must contain letters or digits.");
        if (!Regex.IsMatch(o.Scope, "^[a-z0-9][a-z0-9-]{0,40}$"))
            throw new ConfluenceConfigException($"Invalid scope '{o.Scope}': use 'main' or a short slug such as 'mr-42'.");
        if (o.ParentId is not null && !Regex.IsMatch(o.ParentId, "^[0-9]+$"))
            throw new ConfluenceConfigException("Confluence parent id must be a numeric page id.");
        if (o.ReportsUrl is not null && !IsHttpUrl(o.ReportsUrl))
            throw new ConfluenceConfigException("Reports URL must be an absolute http(s) URL.");
        if (o.BaseUrl.Length > 0 && !IsHttpUrl(o.BaseUrl))
            throw new ConfluenceConfigException("Confluence base URL must be an absolute http(s) URL.");
        return o with { BaseUrl = o.BaseUrl.TrimEnd('/'), ReportsUrl = o.ReportsUrl?.TrimEnd('/') };
    }

    internal static bool IsHttpUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme is "http" or "https";

    internal static string Slug(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c)) sb.Append(c);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        return sb.ToString().Trim('-');
    }
}
