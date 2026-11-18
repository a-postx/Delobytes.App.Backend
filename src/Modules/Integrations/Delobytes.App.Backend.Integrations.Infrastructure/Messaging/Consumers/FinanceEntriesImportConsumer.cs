using Delobytes.App.Backend.Integrations.Application.Events;
using Delobytes.App.Backend.Integrations.Application.Finance;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers;

/// <summary>
/// Consumes the dispatch event of a finance import run and hands it to the orchestrator.
/// </summary>
public class FinanceEntriesImportConsumer : IConsumer<FinanceEntriesImportRequestedEvent>
{
    private readonly FinanceImportOrchestrator _orchestrator;
    private readonly ILogger<FinanceEntriesImportConsumer> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FinanceEntriesImportConsumer"/> class.
    /// </summary>
    public FinanceEntriesImportConsumer(
        FinanceImportOrchestrator orchestrator,
        ILogger<FinanceEntriesImportConsumer> logger)
    {
        _orchestrator = orchestrator;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task Consume(ConsumeContext<FinanceEntriesImportRequestedEvent> context)
    {
        _logger.LogInformation(
            "Received FinanceEntriesImportRequestedEvent. JobRunId={JobRunId}, ConnectionId={ConnectionId}",
            context.Message.JobRunId,
            context.Message.ConnectionId);

        await _orchestrator.ProcessAsync(
            context.Message.JobRunId,
            context.Message.ConnectionId,
            context.CancellationToken);
    }
}
