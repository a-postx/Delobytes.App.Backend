using System.Text.Json;
using System.Text.Json.Nodes;
using Delobytes.App.Backend.Integrations.Contracts.Models;

namespace Delobytes.App.Backend.Integrations.Infrastructure.ApiClients;

/// <summary>
/// Builds the <c>ChannelSpecificData</c> payload of a card: the channel's own fields in a fixed key
/// order, followed by the normalised photo galleries.
/// </summary>
/// <remarks>
/// The import compares this string byte for byte to decide whether a card changed, so it may hold
/// only values that change when the card itself changes.
/// </remarks>
internal static class ChannelCardSnapshot
{
    /// <summary>
    /// Serializes the channel's stable fields together with the card's photo list.
    /// </summary>
    /// <param name="channelFields">Fields of this channel, in the order they must appear.</param>
    /// <param name="photos">Photo variants of the card, in the order the channel reported them.</param>
    /// <returns>The snapshot string stored on the channel-product link.</returns>
    public static string Build(
        IReadOnlyList<KeyValuePair<string, JsonNode?>> channelFields,
        IReadOnlyList<ChannelProductPhoto> photos)
    {
        JsonObject snapshot = new JsonObject();

        foreach (KeyValuePair<string, JsonNode?> field in channelFields)
        {
            snapshot[field.Key] = field.Value?.DeepClone();
        }

        snapshot["photos"] = BuildPhotos(photos);

        return snapshot.ToJsonString();
    }

    /// <summary>
    /// Converts a channel value to a JSON node using the declared property names of its own type.
    /// </summary>
    /// <param name="value">Value to convert; null stays null.</param>
    /// <returns>The node, or null when the value is null.</returns>
    public static JsonNode? ToNode(object? value)
    {
        if (value == null)
        {
            return null;
        }

        return JsonSerializer.SerializeToNode(value, value.GetType());
    }

    private static JsonArray BuildPhotos(IReadOnlyList<ChannelProductPhoto> photos)
    {
        // Ordered by the slot the photo occupies, then by variant: the snapshot answers "does the
        // gallery look the same", not "did the mapper emit the variants in the same sequence".
        IEnumerable<ChannelProductPhoto> ordered = photos
            .OrderBy(photo => photo.DisplayOrder)
            .ThenBy(photo => photo.SizeVariant, StringComparer.Ordinal);

        JsonArray photosNode = new JsonArray();

        foreach (ChannelProductPhoto photo in ordered)
        {
            photosNode.Add(new JsonObject
            {
                ["order"] = photo.DisplayOrder,
                ["variant"] = photo.SizeVariant,
                ["url"] = photo.Url
            });
        }

        return photosNode;
    }
}
