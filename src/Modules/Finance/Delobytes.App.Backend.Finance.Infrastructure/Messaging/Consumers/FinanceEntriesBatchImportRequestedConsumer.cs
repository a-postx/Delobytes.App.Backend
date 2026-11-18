using Delobytes.App.Backend.Finance.Application.Ingestion;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Finance.Infrastructure.Messaging.Consumers;

/// <summary>
/// Consumes a batch of channel finance entries and hands it to
/// <see cref="FinanceEntriesBatchImporter"/>. The tenant and correlation are propagated by the
/// shared consume filters, so the message itself carries neither.
/// </summary>
public class FinanceEntriesBatchImportRequestedConsumer : IConsumer<FinanceEntriesBatchImportRequestedEvent>
{
    private readonly FinanceEntriesBatchImporter _importer;
    private readonly ILogger<FinanceEntriesBatchImportRequestedConsumer> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FinanceEntriesBatchImportRequestedConsumer"/> class.
    /// </summary>
    /// <param name="importer">Batch importer.</param>
    /// <param name="logger">Logger instance.</param>
    public FinanceEntriesBatchImportRequestedConsumer(
        FinanceEntriesBatchImporter importer,
        ILogger<FinanceEntriesBatchImportRequestedConsumer> logger)
    {
        _importer = importer;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task Consume(ConsumeContext<FinanceEntriesBatchImportRequestedEvent> context)
    {
        _logger.LogInformation(
            "Received FinanceEntriesBatchImportRequestedEvent. JobRunId={JobRunId}, BatchId={BatchId}, EntriesCount={EntriesCount}",
            context.Message.JobRunId,
            context.Message.BatchId,
            context.Message.Entries.Count);

        await _importer.ProcessAsync(context.Message, context.CancellationToken);
    }
}
