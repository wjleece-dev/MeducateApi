using System.Text.Json;
using System.Text.Json.Nodes;
using Meducate.Domain.Entities;

namespace Meducate.API.Infrastructure;

// The normal topic response plus where it came from: `source` (provider, pages, when the text was last fetched) and
// `evidence` (for each list item the model said the source backs: the passage, whether it was found word for word,
// and the page it came from). Opt-in through ?include=evidence so the default response stays small.
internal static class TopicEvidenceView
{
    internal static bool Requested(string? include) =>
        include is not null && include.Split(',', StringSplitOptions.TrimEntries).Contains("evidence", StringComparer.OrdinalIgnoreCase);

    internal static JsonObject Build(HealthTopic topic, JsonSerializerOptions options)
    {
        var node = JsonSerializer.SerializeToNode(topic, options) as JsonObject ?? [];
        var (observations, factors, actions) = HealthTopicJsonConverter.GetFieldNames(topic.TopicType);

        var links = new JsonArray();
        foreach (var link in topic.SourceLinks ?? [])
            links.Add(new JsonObject { ["title"] = link.Title, ["url"] = link.Url });

        node["source"] = new JsonObject
        {
            ["name"] = "MedlinePlus",
            ["pages"] = links,
            ["retrievedAt"] = topic.LastSourceRefresh,
        };

        var evidence = new JsonArray();
        foreach (var e in topic.Evidence ?? [])
        {
            var list = e.Field switch { "observations" => observations, "factors" => factors, _ => actions };
            evidence.Add(new JsonObject
            {
                ["list"] = list,
                ["item"] = e.Item,
                ["match"] = e.Match,
                ["quote"] = e.Quote,
                ["section"] = e.Section,
                ["url"] = e.Url,
            });
        }

        node["evidence"] = evidence;
        return node;
    }
}
