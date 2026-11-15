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
/// only information that changes when the card itself changes. Two things are therefore removed:
/// values a marketplace refreshes on its own (Wildberries' <c>updatedAt</c>, the raw photo block
/// with its cache-busting variants), and the order in which a marketplace happens to list a
/// collection. Both marketplaces permute their attribute arrays from response to response without
/// the seller editing anything, so collections are ordered by a stable key before serialization.
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

    /// <summary>
    /// Converts a collection quoted by the channel into a JSON array ordered by
    /// <paramref name="orderKey"/>.
    /// </summary>
    /// <remarks>
    /// The order of the channel's own collection is not part of the card: Wildberries permutes its
    /// attribute array between two responses of an unchanged card, which is what made the import
    /// report those cards as updated on every run. Ordering by a stable key keeps the snapshot
    /// sensitive to the content and blind to the permutation. Elements that the key cannot
    /// distinguish keep a deterministic order through <paramref name="thenByKey"/> or, failing
    /// that, the order they arrived in.
    /// </remarks>
    /// <typeparam name="T">Element type of the collection.</typeparam>
    /// <param name="items">Collection as the channel reported it; null stays null.</param>
    /// <param name="orderKey">Stable identity of an element.</param>
    /// <param name="thenByKey">Optional second key, applied when the first one ties.</param>
    /// <returns>The ordered JSON array, or null when the collection is null.</returns>
    public static JsonNode? ToOrderedArray<T>(
        IEnumerable<T>? items,
        Func<T, long?> orderKey,
        Func<T, long?>? thenByKey = null)
    {
        if (items == null)
        {
            return null;
        }

        // Numeric, not textual: as text "10096" sorts before "85", which is a stable order but not
        // an obvious one to whoever reads the stored snapshot to diagnose a change.
        IOrderedEnumerable<T> ordered = items
            .OrderBy(item => orderKey(item) ?? long.MinValue)
            .ThenBy(item => thenByKey == null ? 0L : thenByKey(item) ?? long.MinValue);

        JsonArray array = new JsonArray();

        foreach (T item in ordered)
        {
            array.Add(ToNode(item));
        }

        return array;
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
