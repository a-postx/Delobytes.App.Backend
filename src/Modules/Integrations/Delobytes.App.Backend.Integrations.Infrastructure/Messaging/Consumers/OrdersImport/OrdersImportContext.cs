using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers.OrdersImport;

/// <summary>
/// Immutable context for one orders import run: identity, capability, and the read window.
/// All required dependencies are resolved and validated by the orchestrator before any fetcher
/// receives it, so every property is non-nullable.
/// </summary>
public sealed class OrdersImportContext
{
    public OrdersImportContext(
        JobRun jobRun,
        Connection connection,
        IOrdersSource ordersSource,
        OrdersBackfillState backfillState,
        DateTimeOffset deadline,
        DateTimeOffset windowTo,
        bool isBackfill)
    {
        JobRun = jobRun ?? throw new ArgumentNullException(nameof(jobRun));
        Connection = connection ?? throw new ArgumentNullException(nameof(connection));
        OrdersSource = ordersSource ?? throw new ArgumentNullException(nameof(ordersSource));
        BackfillState = backfillState ?? throw new ArgumentNullException(nameof(backfillState));
        Deadline = deadline;
        WindowTo = windowTo;
        IsBackfill = isBackfill;
    }

    public JobRun JobRun { get; }

    public Connection Connection { get; }

    public IOrdersSource OrdersSource { get; }

    public OrdersBackfillState BackfillState { get; }

    public DateTimeOffset Deadline { get; }

    public DateTimeOffset WindowTo { get; }

    public bool IsBackfill { get; }
}
