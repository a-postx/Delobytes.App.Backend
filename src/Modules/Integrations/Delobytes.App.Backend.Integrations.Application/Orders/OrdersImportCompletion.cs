using Delobytes.App.Backend.Integrations.Domain.Entities;

namespace Delobytes.App.Backend.Integrations.Application.Orders;

/// <summary>
/// Result of handing one delivery to <see cref="OrdersBatchAggregator"/>: the run the delivery
/// belongs to, and whether the caller still owes downstream consumers its completion event.
/// </summary>
/// <param name="JobRun">Run the delivery belongs to, in its post-call state.</param>
/// <param name="ShouldPublishCompletedEvent">
/// True when the run is finalised and the completion event must be published — either because this
/// call finalised it, or because a redelivered message found it already terminal.
/// </param>
public sealed record OrdersImportCompletion(JobRun JobRun, bool ShouldPublishCompletedEvent);
