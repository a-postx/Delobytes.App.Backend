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

    /// <summary>
    /// Number of rows the source refused to map for lack of identity. Carried so the caller can
    /// report it: the fetch itself already failed before returning when this is non-zero.
    /// </summary>
    public required int RowsWithoutIdentity { get; init; }
}
