using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers.OrdersImport;

/// <summary>
/// Immutable context for one orders import run: identity, capability, and the read window.
/// All required dependencies are resolved and validated by the orchestrator before any fetcher
/// receives it, so every property is non-nullable except <see cref="Schema"/>, which is simply
/// absent for channels (Wildberries) that do not distinguish schemas.
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
        bool isBackfill,
        OrdersSchema? schema = null)
    {
        JobRun = jobRun ?? throw new ArgumentNullException(nameof(jobRun));
        Connection = connection ?? throw new ArgumentNullException(nameof(connection));
        OrdersSource = ordersSource ?? throw new ArgumentNullException(nameof(ordersSource));
        BackfillState = backfillState ?? throw new ArgumentNullException(nameof(backfillState));
        Deadline = deadline;
        WindowTo = windowTo;
        IsBackfill = isBackfill;
        JobType = jobRun.JobType;
        Schema = schema;
    }

    public JobRun JobRun { get; }

    public Connection Connection { get; }

    public IOrdersSource OrdersSource { get; }

    public OrdersBackfillState BackfillState { get; }

    public DateTimeOffset Deadline { get; }

    public DateTimeOffset WindowTo { get; }

    public bool IsBackfill { get; }

    /// <summary>
    /// Gets the orders job type this run belongs to. Mirrors <see cref="JobRun.JobType"/> so
    /// fetchers and cursor lookups do not need to reach through <see cref="JobRun"/> for it.
    /// </summary>
    public JobType JobType { get; }

    /// <summary>
    /// Gets the channel-specific orders schema this run requests (e.g. FBO/FBS for Ozon), or
    /// null for channels that do not distinguish one.
    /// </summary>
    public OrdersSchema? Schema { get; }
}
