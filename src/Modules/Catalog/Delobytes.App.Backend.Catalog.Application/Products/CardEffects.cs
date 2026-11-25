using Delobytes.App.Backend.Catalog.Application.Interfaces;
using Delobytes.App.Backend.Catalog.Domain.Entities;

namespace Delobytes.App.Backend.Catalog.Application.Products;

/// <summary>
/// Work a saved card leaves for after the batch transaction commits: photos to download and the
/// link to announce to Sales. Collected per card and enqueued only once the card's savepoint has
/// been released, so a card that was rolled back leaves no effect behind.
/// </summary>
public sealed record CardEffects
{
    /// <summary>Card identifier, for logging.</summary>
    public string ExternalProductId { get; init; } = string.Empty;

    /// <summary>
    /// Product whose own gallery is reconciled; set only when this card created the product.
    /// </summary>
    public Product? CanonicalPhotoOwner { get; init; }

    /// <summary>Photo sources for <see cref="CanonicalPhotoOwner"/>.</summary>
    public IReadOnlyList<MarketplacePhotoSource> CanonicalPhotoSources { get; init; } = Array.Empty<MarketplacePhotoSource>();

    /// <summary>Link whose gallery is reconciled; every card refreshes the gallery of its channel.</summary>
    public ChannelProduct? ChannelPhotoOwner { get; init; }

    /// <summary>
    /// Photo sources for <see cref="ChannelPhotoOwner"/>. Empty is meaningful: it is how a card
    /// whose photos were all removed in the marketplace reaches reconciliation.
    /// </summary>
    public IReadOnlyList<MarketplacePhotoSource> ChannelPhotoSources { get; init; } = Array.Empty<MarketplacePhotoSource>();

    /// <summary>Link to announce once it is committed, or <c>null</c> when the card wrote none.</summary>
    public ChannelProduct? LinkToPublish { get; init; }
}

/// <summary>
/// Outcome of one card: the counter it reports and the effects that follow its commit. Effects are
/// <c>null</c> for a card that deliberately wrote nothing.
/// </summary>
/// <param name="Status">Counter to record.</param>
/// <param name="Effects">Post-commit work, or <c>null</c>.</param>
public sealed record CardImportOutcome(ImportStatus Status, CardEffects? Effects);
