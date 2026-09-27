using Delobytes.App.Backend.Integrations.Contracts.Events;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers;

/// <summary>
/// Consumer for orchestrating products import with cursor-based pagination.
/// </summary>
public class ProcessProductsImportConsumer
{
    private readonly ILogger<ProcessProductsImportConsumer> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProcessProductsImportConsumer"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    public ProcessProductsImportConsumer(ILogger<ProcessProductsImportConsumer> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Processes the ProductsImportRequestedEvent message.
    /// </summary>
    /// <param name="message">Event message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task ProcessAsync(ProductsImportRequestedEvent message, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "ProcessProductsImportConsumer received ProductsImportRequestedEvent. SyncJobId={SyncJobId}, ConnectionId={ConnectionId}",
            message.SyncJobId,
            message.ConnectionId);

        // TODO: Implement business logic in later stages
        // - Load Connection with API key
        // - Call WB API in cursor-based loop
        // - Publish ProductImportBatchRequestedEvent for each batch
        // - Save NextCursor to SyncJob
        // - Publish ProductsImportCompletedEvent when done

        return Task.CompletedTask;
    }
}
