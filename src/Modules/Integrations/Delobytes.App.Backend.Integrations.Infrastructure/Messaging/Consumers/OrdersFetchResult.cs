using Delobytes.App.Backend.Integrations.Contracts.Models;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers;

/// <summary>
/// Merged result of the bisection performed by <see cref="ProcessOrdersImportConsumer"/>: every
/// distinct order of the requested window plus the number of HTTP requests it took to collect them.
/// </summary>
internal sealed record OrdersFetchResult
{
    public required List<ChannelOrderSnapshot> Orders { get; init; }

    public required int RequestsIssued { get; init; }
}
