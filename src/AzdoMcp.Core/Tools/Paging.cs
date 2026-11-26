using System.Text.Json.Nodes;
using AzdoMcp.Core.Azdo;

namespace AzdoMcp.Core.Tools;

/// <summary>Items collected from several pages.</summary>
internal sealed record Collected(List<JsonNode> Items, int Processed, bool Truncated);

/// <summary>What to do with one item while walking pages.</summary>
internal enum Walk
{
    Keep,
    Skip,

    /// <summary>The list is ordered and this item is past the wanted range: stop without truncation.</summary>
    Stop,
}

internal static class Paging
{
    /// <summary>
    /// Walks a $top/$skip list until it ends, <paramref name="decide"/> says stop, or <paramref name="limit"/> items are kept.
    /// <paramref name="url"/> receives (skip, top).
    /// </summary>
    public static async Task<Collected> SkipAsync(AzdoClient client, Func<int, int, string> url, int pageSize, int limit, Func<JsonNode, Walk>? decide, CancellationToken ct, int maxProcessed = 20000)
    {
        var kept = new List<JsonNode>();
        var processed = 0;
        for (var skip = 0; ; skip += pageSize)
        {
            var res = await client.GetAsync(url(skip, pageSize), ct).ConfigureAwait(false);
            var page = res?["value"]?.AsArray() ?? [];
            foreach (var item in page)
            {
                if (item is null)
                {
                    continue;
                }

                processed++;
                var d = decide?.Invoke(item) ?? Walk.Keep;
                if (d == Walk.Stop)
                {
                    return new(kept, processed, false);
                }

                if (d == Walk.Keep)
                {
                    if (kept.Count == limit)
                    {
                        return new(kept, processed, true);
                    }

                    kept.Add(item.DeepClone());
                }
            }

            if (page.Count < pageSize || processed >= maxProcessed)
            {
                return new(kept, processed, page.Count >= pageSize);
            }
        }
    }

    /// <summary>Walks a list paged by the x-ms-continuationtoken header. <paramref name="url"/> receives the token (null first).</summary>
    public static async Task<Collected> TokenAsync(AzdoClient client, Func<string?, string> url, int limit, Func<JsonNode, Walk>? decide, CancellationToken ct, int maxPages = 200)
    {
        var kept = new List<JsonNode>();
        var processed = 0;
        string? token = null;
        for (var pages = 0; pages < maxPages; pages++)
        {
            var (res, next) = await client.GetPageAsync(url(token), ct).ConfigureAwait(false);
            foreach (var item in res?["value"]?.AsArray() ?? [])
            {
                if (item is null)
                {
                    continue;
                }

                processed++;
                var d = decide?.Invoke(item) ?? Walk.Keep;
                if (d == Walk.Stop)
                {
                    return new(kept, processed, false);
                }

                if (d == Walk.Keep)
                {
                    if (kept.Count == limit)
                    {
                        return new(kept, processed, true);
                    }

                    kept.Add(item.DeepClone());
                }
            }

            token = next ?? Shape.Str(res?["continuationToken"]);
            if (token is null)
            {
                return new(kept, processed, false);
            }
        }

        return new(kept, processed, true);
    }

    /// <summary>
    /// Splits [from, to] into windows of at most <paramref name="days"/> days, newest first, without gaps.
    /// The first window is <paramref name="firstDays"/> long so "the newest N" rarely needs a whole week.
    /// </summary>
    public static IEnumerable<(DateTimeOffset From, DateTimeOffset To)> Windows(DateTimeOffset from, DateTimeOffset to, int days = 7, int firstDays = 1)
    {
        if (to < from)
        {
            yield break;
        }

        var end = to;
        var first = true;
        while (end > from)
        {
            var start = end.AddDays(first ? -Math.Min(firstDays, days) : -days);
            first = false;
            if (start < from)
            {
                start = from;
            }

            yield return (start, end);
            end = start;
        }
    }

    public static string Q(string value) => Uri.EscapeDataString(value);
}
