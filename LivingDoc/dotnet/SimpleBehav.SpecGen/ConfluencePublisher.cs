namespace SimpleBehav.SpecGen;

public sealed class ConfluencePublishException(string message) : Exception(message);

public sealed record PublishedPage(string Key, string Title, string Id, string Url, bool Created);

public sealed record PublishResult(
    ConfluenceSpace Space, IReadOnlyList<PublishedPage> Pages, IReadOnlyList<RemotePage> Stale, bool Pruned);

/// <summary>
/// Idempotent publish of rendered pages. A page is only ever overwritten when it carries this run's ownership
/// label; a same-titled page without it is a conflict and nothing is written. MR previews use a different
/// scope, hence different titles and labels, so they can never touch the default branch's pages.
/// </summary>
public sealed class ConfluencePublisher(IConfluenceClient client, ConfluenceOptions options, Action<string>? log = null)
{
    private readonly Action<string> _log = log ?? (_ => { });

    public async Task<PublishResult> PublishAsync(IReadOnlyList<ConfluencePage> pages, CancellationToken ct = default)
    {
        var space = await ResolveSpaceAsync(ct);

        // Check every title first so a conflict can't leave a half-published set behind.
        var existing = new Dictionary<string, RemotePage?>();
        var conflicts = new List<string>();
        foreach (var page in pages)
        {
            var found = await client.FindPageAsync(space.Key, page.Title, ct);
            existing[page.Key] = found;
            if (found is not null && !found.Labels.Contains(options.OwnerLabel))
                conflicts.Add($"'{page.Title}' ({found.Url})");
        }
        if (conflicts.Count > 0)
            throw new ConfluencePublishException(
                $"Refusing to overwrite pages in space {space.Key} that this publisher does not own (missing label '{options.OwnerLabel}'): " +
                string.Join(", ", conflicts) + ". Rename or remove them, or change the Confluence name/scope.");

        var published = new List<PublishedPage>();
        string? rootId = null;
        foreach (var page in pages.OrderBy(p => p.IsRoot ? 0 : 1))
        {
            var found = existing[page.Key];
            RemotePage result;
            if (found is null)
            {
                result = await client.CreatePageAsync(space.Key, page.Title, page.Body,
                    page.IsRoot ? options.ParentId : rootId, ct);
                _log($"Created '{page.Title}'");
            }
            else
            {
                result = await client.UpdatePageAsync(found, page.Body, ct);
                _log($"Updated '{page.Title}'");
            }

            var missing = new[] { ConfluenceOptions.ManagedLabel, options.OwnerLabel }.Where(l => !result.Labels.Contains(l)).ToList();
            if (missing.Count > 0) await client.AddLabelsAsync(result.Id, missing, ct);

            if (page.IsRoot) rootId = result.Id;
            published.Add(new PublishedPage(page.Key, page.Title, result.Id, result.Url, found is null));
        }

        var current = published.Select(p => p.Id).ToHashSet();
        var stale = (await client.FindPagesByLabelAsync(space.Key, options.OwnerLabel, ct))
            .Where(p => !current.Contains(p.Id)).ToList();
        foreach (var s in stale)
        {
            if (options.Prune)
            {
                await client.TrashPageAsync(s.Id, ct);
                _log($"Trashed stale '{s.Title}'");
            }
            else _log($"Stale page left in place (use --confluence-prune to trash it): '{s.Title}'");
        }

        return new PublishResult(space, published, stale, options.Prune);
    }

    /// <summary>
    /// The configured value is tried as a space key; failing that, as an exact (case-insensitive) space name.
    /// No match, several matches, or a personal space is an error — never a fallback to some other space.
    /// </summary>
    public async Task<ConfluenceSpace> ResolveSpaceAsync(CancellationToken ct = default)
    {
        var wanted = options.Space;
        var space = await client.GetSpaceAsync(wanted, ct);
        if (space is null)
        {
            var byName = (await client.ListSpacesAsync(ct))
                .Where(s => string.Equals(s.Name, wanted, StringComparison.OrdinalIgnoreCase)).ToList();
            space = byName.Count switch
            {
                1 => byName[0],
                0 => throw new ConfluencePublishException(
                    $"Confluence space '{wanted}' was not found by key or by name, so nothing was published. " +
                    "Set CONFLUENCE_SPACE (or --confluence-space) to the space key."),
                _ => throw new ConfluencePublishException(
                    $"Confluence space name '{wanted}' is ambiguous (keys: {string.Join(", ", byName.Select(s => s.Key))}). " +
                    "Set CONFLUENCE_SPACE to the space key."),
            };
        }

        if (space.Type.Equals("personal", StringComparison.OrdinalIgnoreCase))
            throw new ConfluencePublishException($"Space {space.Key} is a personal space; refusing to publish living documentation there.");

        _log($"Publishing to space {space.Key} ({space.Name})");
        return space;
    }
}
