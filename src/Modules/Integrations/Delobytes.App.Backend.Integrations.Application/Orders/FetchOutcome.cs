using Delobytes.App.Backend.Integrations.Contracts.Models;

namespace Delobytes.App.Backend.Integrations.Application.Orders;

/// <summary>
/// Immutable result of one fetch. A stop is a value, not an exception, and each read case carries
/// only the fields its branch owns: a delta read reports the orders and the watermark it earned, a
/// backfill read reports the days' totals and the handover watermark.
/// </summary>
public abstract record FetchOutcome
{
    public required int RequestsIssued { get; init; }

    public sealed record Stopped : FetchOutcome
    {
        public required StopCause Cause { get; init; }

        public required DateTimeOffset ResumeAt { get; init; }
    }

    public sealed record DeltaRead : FetchOutcome
    {
        public required IReadOnlyList<ChannelOrderSnapshot> Orders { get; init; }

        public DateTimeOffset? MaxChangedAt { get; init; }
    }

    public sealed record BackfillRead : FetchOutcome
    {
        public required int OrdersFetched { get; init; }

        public required bool IsCompleted { get; init; }

        public DateTimeOffset? HandoverWatermark { get; init; }
    }
}
