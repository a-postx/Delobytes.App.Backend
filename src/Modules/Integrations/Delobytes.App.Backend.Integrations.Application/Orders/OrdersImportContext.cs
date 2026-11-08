using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Application.Options;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;

namespace Delobytes.App.Backend.Integrations.Application.Orders;

/// <summary>
/// Immutable context for one orders import run: identity, capability, tuning and the read window.
/// Every dependency is resolved by the orchestrator before any fetcher receives the context.
/// </summary>
public sealed class OrdersImportContext
{
    public required JobRun JobRun { get; init; }

    public required Connection Connection { get; init; }

    public required IOrdersSource OrdersSource { get; init; }

    public required OrdersBackfillState BackfillState { get; init; }

    public required OrdersImportTuning Tuning { get; init; }

    public required DateTimeOffset Deadline { get; init; }

    public required DateTimeOffset WindowTo { get; init; }

    public required bool IsBackfill { get; init; }

    public required OrdersSchema? Schema { get; init; }

    public JobType JobType
    {
        get
        {
            return JobRun.JobType;
        }
    }

    public int MaxRequestsPerRun
    {
        get
        {
            return Tuning.MaxRequestsPerRun;
        }
    }
}
