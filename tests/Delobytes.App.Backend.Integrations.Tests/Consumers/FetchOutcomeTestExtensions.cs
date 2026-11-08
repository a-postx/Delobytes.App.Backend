using System;
using Delobytes.App.Backend.Integrations.Application.Orders;

namespace Delobytes.App.Backend.Integrations.Tests.Consumers;

/// <summary>
/// Typed access to a <see cref="FetchOutcome"/> case. An assertion that reads a case's field has to
/// name the case first, so a test cannot accidentally read a field the returned case does not own.
/// </summary>
internal static class FetchOutcomeTestExtensions
{
    public static FetchOutcome.Stopped Stopped(this FetchOutcome outcome)
    {
        return outcome as FetchOutcome.Stopped
            ?? throw new InvalidOperationException($"Expected a stopped outcome, got {outcome.GetType().Name}.");
    }

    public static FetchOutcome.DeltaRead Delta(this FetchOutcome outcome)
    {
        return outcome as FetchOutcome.DeltaRead
            ?? throw new InvalidOperationException($"Expected a delta read, got {outcome.GetType().Name}.");
    }

    public static FetchOutcome.BackfillRead Backfill(this FetchOutcome outcome)
    {
        return outcome as FetchOutcome.BackfillRead
            ?? throw new InvalidOperationException($"Expected a backfill read, got {outcome.GetType().Name}.");
    }
}
