using Delobytes.App.Backend.Catalog.Application.Interfaces;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using Delobytes.App.Backend.Integrations.Contracts.Models;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Catalog.Application.Products;

/// <summary>
/// Imports one batch of marketplace product cards into the catalog: resolves each card to a known
/// product (channel link, then the channel's barcodes, then the canonical barcodes, then the seller
/// article), synchronises the channel's barcodes and packing unit, queues photos and channel links
/// for after the commit, and reports batch counters.
/// </summary>
/// <remarks>
/// The importer owns the phases of the batch, not the rules inside them: matching, card projection,
/// barcodes, packing units, numbering, and the post-commit effects live in their own classes, so
/// what a card writes is one routine shared by the create, link and update paths.
/// </remarks>
public class ProductBatchImporter
{
    private readonly IProductImportStore _store;
    private readonly ProductCardMatcher _matcher;
    private readonly ChannelProductUpserter _upserter;
    private readonly CardBarcodePolicy _barcodes;
    private readonly ProductCodeResolver _codes;
    private readonly ProductImportEffectsPublisher _effects;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<ProductBatchImporter> _logger;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProductBatchImporter"/> class.
    /// </summary>
    /// <param name="store">Persistence port of the import.</param>
    /// <param name="matcher">Resolves a card to a catalogue product.</param>
    /// <param name="upserter">Writes a card onto a channel-product link.</param>
    /// <param name="barcodes">Barcode rules of the import.</param>
    /// <param name="codes">Issues product codes and SKUs.</param>
    /// <param name="effects">Post-commit photos and link publication.</param>
    /// <param name="eventPublisher">Event publisher.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="timeProvider">Clock source.</param>
    public ProductBatchImporter(
        IProductImportStore store,
        ProductCardMatcher matcher,
        ChannelProductUpserter upserter,
        CardBarcodePolicy barcodes,
        ProductCodeResolver codes,
        ProductImportEffectsPublisher effects,
        IEventPublisher eventPublisher,
        ILogger<ProductBatchImporter> logger,
        TimeProvider timeProvider)
    {
        _store = store;
        _matcher = matcher;
        _upserter = upserter;
        _barcodes = barcodes;
        _codes = codes;
        _effects = effects;
        _eventPublisher = eventPublisher;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Processes the ProductBatchImportRequestedEvent message.
    /// </summary>
    /// <param name="message">Event message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ImportAsync(ProductBatchImportRequestedEvent message, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "ProductBatchImporter started processing batch. JobRunId={JobRunId}, ConnectionId={ConnectionId}, ChannelId={ChannelId}, CardsCount={CardsCount}, IsLastBatch={IsLastBatch}",
            message.JobRunId,
            message.ConnectionId,
            message.ChannelId,
            message.Cards.Count,
            message.IsLastBatch);

        ProductImportCounters counters = new ProductImportCounters();
        List<CardEffects> effects = new List<CardEffects>();

        using (IProductImportBatchTransaction transaction = await _store.BeginBatchAsync(cancellationToken))
        {
            try
            {
                await ProcessCardsAsync(message, transaction, counters, effects, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process batch. JobRunId={JobRunId}", message.JobRunId);
                await TryRollbackAsync(transaction);

                // No batch result is published here on purpose. MassTransit retries the message, and
                // the retry publishes its own result; a synthetic failure first would leave a
                // phantom failed row for a batch that later succeeded, and any failed row makes the
                // aggregator finalise the whole run as Failed. A batch that exhausts its retries
                // reaches the error queue, and the run watchdog closes the run.
                throw;
            }
        }

        // The transaction is closed: products, channel products, barcodes and packing units are
        // durable. Photos are downloaded and the links are announced now, outside any database
        // transaction, and failures there are logged instead of failing the batch.
        await _effects.PublishAsync(effects, cancellationToken);

        ProductBatchImportCompletedEvent completedEvent = new ProductBatchImportCompletedEvent
        {
            JobRunId = message.JobRunId,
            RecordsProcessed = counters.Processed,
            RecordsCreated = counters.Created,
            RecordsUpdated = counters.Updated,
            RecordsSkipped = counters.Skipped,
            RecordsFailed = counters.Failed,
            ErrorMessage = counters.ErrorMessage,
            IsLastBatch = message.IsLastBatch,
            TotalBatches = message.TotalBatches,
        };

        await _eventPublisher.PublishAsync(completedEvent, cancellationToken);

        _logger.LogInformation(
            "ProductBatchImporter completed batch. JobRunId={JobRunId}, Processed={Processed}, Created={Created}, Updated={Updated}, Skipped={Skipped}, Failed={Failed}",
            message.JobRunId,
            counters.Processed,
            counters.Created,
            counters.Updated,
            counters.Skipped,
            counters.Failed);
    }

    private async Task ProcessCardsAsync(
        ProductBatchImportRequestedEvent message,
        IProductImportBatchTransaction transaction,
        ProductImportCounters counters,
        List<CardEffects> effects,
        CancellationToken cancellationToken)
    {
        Channel? channel = await _store.FindChannelAsync(message.ChannelId, cancellationToken);

        if (channel == null)
        {
            string channelError = $"Channel with Id={message.ChannelId} not found.";
            _logger.LogError(channelError);
            counters.RecordBatchFailure(channelError, message.Cards.Count);

            return;
        }

        foreach (ChannelProductCard card in message.Cards)
        {
            using (IProductImportCardScope cardScope = await transaction.BeginCardAsync(cancellationToken))
            {
                try
                {
                    CardImportOutcome outcome = await ProcessCardAsync(
                        card,
                        message.JobRunId,
                        message.ChannelId,
                        cancellationToken);

                    // Now one flush per card instead of one per batch.
                    await cardScope.SaveAsync(cancellationToken);

                    counters.Record(outcome.Status);

                    // Only a card that reached this point may leave work behind: a card that was
                    // rolled back must not have its photos downloaded or its link announced.
                    if (outcome.Effects != null)
                    {
                        effects.Add(outcome.Effects);
                    }
                }
                catch (Exception ex)
                {
                    // Back to the state before this card, both in PostgreSQL and in the change tracker.
                    await cardScope.RollbackAsync(cancellationToken);

                    counters.RecordCardFailure(card.ExternalProductId, ex.Message);
                    _logger.LogError(ex, "Failed to process card NmId={NmId}", card.ExternalProductId);
                }
            }
        }

        // The full-set reconciliation is NOT done here: this batch cannot see its siblings'
        // uncommitted rows, so it would read every card they have not yet committed as
        // dropped by the channel. Integrations publishes
        // ChannelProductFullSetReconciliationRequestedEvent once every batch of the run has
        // been recorded, and Catalog sweeps then.
    }

    /// <summary>
    /// Resolves a card to a product: the existing channel link first, then a match by barcode, then
    /// a match by the seller article the card carries on any channel. A card that matches nothing
    /// is imported as a new product; an ambiguous match is skipped instead.
    /// </summary>
    private async Task<CardImportOutcome> ProcessCardAsync(
        ChannelProductCard card,
        Guid jobRunId,
        Guid channelId,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();

        // Normalised once, here: the matchers compare these values against trimmed columns, and a
        // stray space would otherwise make a card look like a new product with a new barcode.
        IReadOnlyList<string> barcodes = ProductCardNormalizer.NormalizeBarcodes(card.Barcodes);

        ChannelProduct? existingLink = await _store
            .FindChannelProductWithProductAsync(channelId, card.ExternalProductId, cancellationToken);

        if (existingLink != null)
        {
            bool changed = _upserter.Apply(existingLink, card, barcodes, now);

            return new CardImportOutcome(
                changed ? ImportStatus.Updated : ImportStatus.Skipped,
                BuildEffects(card, existingLink, canonicalPhotoOwner: null));
        }

        // A card with neither barcode nor seller article carries nothing that could identify it.
        // Importing it would create a product per run, so it is treated as a draft of the channel
        // and left out of the catalogue until the channel publishes identifiers.
        if (barcodes.Count == 0 && string.IsNullOrWhiteSpace(card.OfferId))
        {
            _logger.LogInformation(
                "JobRunId={JobRunId}, ChannelId={ChannelId}, ExternalProductId={ExternalProductId}: card has neither barcode nor offer id; treating as draft and skipping.",
                jobRunId,
                channelId,
                card.ExternalProductId);

            return new CardImportOutcome(ImportStatus.Skipped, Effects: null);
        }

        ProductMatch match = await _matcher.MatchAsync(card, barcodes, channelId, cancellationToken);

        if (match.IsAmbiguous)
        {
            // Logged, not recorded as an error: the run's status is derived from RecordsFailed and
            // from a non-empty batch ErrorMessage, so a skipped card must not write one. The warning
            // is what tells an operator why the card is missing from the catalogue.
            _logger.LogWarning(
                "JobRunId={JobRunId}, ChannelId={ChannelId}, ExternalProductId={ExternalProductId}: the card's identifiers point at several products and no single one can be chosen; skipping the card.",
                jobRunId,
                channelId,
                card.ExternalProductId);

            return new CardImportOutcome(ImportStatus.Skipped, Effects: null);
        }

        if (match.Product != null)
        {
            return await LinkExistingProductAsync(
                match.Product,
                card,
                jobRunId,
                channelId,
                barcodes,
                now,
                cancellationToken);
        }

        return await CreateNewProductAsync(card, channelId, barcodes, now, cancellationToken);
    }

    /// <summary>
    /// Links a product found by barcode or by seller article to the importing channel, for the case
    /// where the card's external id is not known yet. A product may hold only one link per channel
    /// (unique index on ProductId + ChannelId), so when a link already exists the card is skipped
    /// with a warning rather than a second link created: creating one would violate the index and
    /// roll back the whole batch, and overwriting the existing link would make its data flip
    /// between the two cards each time either one is imported.
    /// </summary>
    /// <remarks>
    /// The product exists already, so its canonical barcodes are not touched: they were fixed when
    /// it was created and belong to the operator from then on.
    /// </remarks>
    private async Task<CardImportOutcome> LinkExistingProductAsync(
        Product product,
        ChannelProductCard card,
        Guid jobRunId,
        Guid channelId,
        IReadOnlyList<string> barcodes,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ChannelProduct? existingLink = await _store
            .FindChannelProductLinkAsync(product.Id, channelId, cancellationToken);

        if (existingLink != null)
        {
            // The usual cause is two cards of one channel reporting the same barcode: the first one
            // created the product, the second matches it and has nowhere to attach. Logged rather
            // than recorded as an error, for the same reason as the ambiguous match above.
            _logger.LogWarning(
                "JobRunId={JobRunId}, ChannelId={ChannelId}, ExternalProductId={ExternalProductId} matched ProductId={ProductId}, but that product is already linked to this channel via ExternalProductId={LinkedExternalProductId}; two cards of this channel probably report the same barcode. Skipping the card: one product can hold only one link per channel.",
                jobRunId,
                channelId,
                card.ExternalProductId,
                product.Id,
                existingLink.ExternalProductId);

            return new CardImportOutcome(ImportStatus.Skipped, Effects: null);
        }

        ChannelProduct link = _upserter.Create(product, card, channelId, barcodes, now);

        return new CardImportOutcome(
            ImportStatus.Updated,
            BuildEffects(card, link, canonicalPhotoOwner: null));
    }

    private async Task<CardImportOutcome> CreateNewProductAsync(
        ChannelProductCard card,
        Guid channelId,
        IReadOnlyList<string> barcodes,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // Product.Sku is unique per tenant and is copied from the marketplace vendor code when
        // that value is free. It is never a matching key: a vendor code another product already
        // holds — typically a hand-made product with no channel link — must not abort the card,
        // so the product falls back to its own code, which is unique by construction unless a user
        // typed that code in by hand before numbering existed.
        (string code, string sku) = await _codes.ResolveAsync(card.OfferId, cancellationToken);

        Product product = new Product
        {
            Code = code,
            Sku = sku,
            Name = ProductCardNormalizer.TrimName(card.Name),
            Description = ProductCardNormalizer.TrimDescription(card.Description),
            CreationSource = ProductCardNormalizer.ResolveCreationSource(card),
            CreatedAt = now,
        };

        _store.AddProduct(product);

        ChannelProduct link = _upserter.Create(product, card, channelId, barcodes, now);

        // The only place canonical barcodes are written by an import: this is the product's first
        // appearance, so its card's values are the best statement of its identity the catalogue will
        // ever get. Later imports keep the channel's values on the link instead.
        await _barcodes.AssignCanonicalAsync(
            product,
            channelId,
            card.ExternalProductId,
            barcodes,
            now,
            cancellationToken);

        return new CardImportOutcome(
            ImportStatus.Created,
            BuildEffects(card, link, canonicalPhotoOwner: product));
    }

    /// <summary>
    /// Collects what the saved card leaves for after the commit. The canonical gallery is written
    /// only when the product is created, while every path refreshes the gallery of its own channel
    /// and re-announces the link: a link created before this event existed, or one whose earlier
    /// publication failed, is healed by the next import of the same card.
    /// </summary>
    private static CardEffects BuildEffects(
        ChannelProductCard card,
        ChannelProduct link,
        Product? canonicalPhotoOwner)
    {
        List<MarketplacePhotoSource> sources = ProductCardNormalizer.BuildPhotoSources(card);

        return new CardEffects
        {
            ExternalProductId = card.ExternalProductId,
            CanonicalPhotoOwner = canonicalPhotoOwner,
            CanonicalPhotoSources = canonicalPhotoOwner is null
                ? Array.Empty<MarketplacePhotoSource>()
                : sources,
            ChannelPhotoOwner = link,
            ChannelPhotoSources = sources,
            LinkToPublish = link,
        };
    }

    private async Task TryRollbackAsync(IProductImportBatchTransaction transaction)
    {
        // CancellationToken.None on purpose: a cancelled consume must still release the
        // transaction, and a rollback that itself fails must not replace the original error.
        try
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
        catch (Exception rollbackException)
        {
            _logger.LogWarning(rollbackException, "Failed to roll back the failed batch.");
        }
    }
}
