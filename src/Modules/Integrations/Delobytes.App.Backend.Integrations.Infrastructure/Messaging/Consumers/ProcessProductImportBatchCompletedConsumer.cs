using Delobytes.App.Backend.Integrations.Contracts.Events;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers;

/// <summary>
/// Consumer for processing product import batch completion events.
/// </summary>
public class ProcessProductImportBatchCompletedConsumer
{
    private readonly ILogger<ProcessProductImportBatchCompletedConsumer> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProcessProductImportBatchCompletedConsumer"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    public ProcessProductImportBatchCompletedConsumer(ILogger<ProcessProductImportBatchCompletedConsumer> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Processes the ProductImportBatchCompletedEvent message.
    /// </summary>
    /// <param name="message">Event message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task ProcessAsync(ProductImportBatchCompletedEvent message, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "ProcessProductImportBatchCompletedConsumer received ProductImportBatchCompletedEvent. SyncJobId={SyncJobId}, RecordsProcessed={RecordsProcessed}, RecordsCreated={RecordsCreated}, RecordsUpdated={RecordsUpdated}, RecordsSkipped={RecordsSkipped}, RecordsFailed={RecordsFailed}",
            message.SyncJobId,
            message.RecordsProcessed,
            message.RecordsCreated,
            message.RecordsUpdated,
            message.RecordsSkipped,
            message.RecordsFailed);

        // TODO: Implement business logic in later stages
        // - Load SyncJob by Id
        // - Update RecordsProcessed and RecordsImported counters
        // - If error, log it
        // - Save changes

        return Task.CompletedTask;
    }
}
