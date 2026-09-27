using Delobytes.App.Backend.Integrations.Contracts.Events;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Messaging.Consumers;

/// <summary>
/// Consumer for importing product batches into Catalog module.
/// </summary>
public class ImportProductBatchConsumer
{
    private readonly ILogger<ImportProductBatchConsumer> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ImportProductBatchConsumer"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    public ImportProductBatchConsumer(ILogger<ImportProductBatchConsumer> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Processes the ProductImportBatchRequestedEvent message.
    /// </summary>
    /// <param name="message">Event message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task ProcessAsync(ProductImportBatchRequestedEvent message, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "ImportProductBatchConsumer received ProductImportBatchRequestedEvent. SyncJobId={SyncJobId}, ConnectionId={ConnectionId}, ChannelId={ChannelId}, CardsCount={CardsCount}, IsLastBatch={IsLastBatch}",
            message.SyncJobId,
            message.ConnectionId,
            message.ChannelId,
            message.Cards.Count,
            message.IsLastBatch);

        // TODO: Implement business logic in later stages
        // - For each card:
        //   - Upsert Product (by Sku; CreationSource = WildberriesImport)
        //   - Upsert ProductBarcode (Type = "WB")
        //   - Upsert ChannelProduct (by ChannelId + ExternalProductId; IsActive = true)
        // - If IsLastBatch:
        //   - Mark ChannelProduct.IsActive = false for products not in the current full set
        // - Publish ProductImportBatchCompletedEvent with statistics

        return Task.CompletedTask;
    }
}
