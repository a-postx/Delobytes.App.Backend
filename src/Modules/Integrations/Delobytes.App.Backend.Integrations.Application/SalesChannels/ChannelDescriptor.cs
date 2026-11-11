using Delobytes.App.Backend.Integrations.Application.Jobs;

namespace Delobytes.App.Backend.Integrations.Application.SalesChannels;

/// <summary>
/// Everything the application layer needs to know about one marketplace channel. Capabilities are
/// declared at this level — which kinds of import the channel supports and whether its orders jobs
/// must be serialised.
/// </summary>
/// <param name="Code">Channel code, matching the system channel template code.</param>
/// <param name="DisplayName">Human-readable channel name.</param>
/// <param name="SupportedJobKinds">Import kinds the channel serves.</param>
/// <param name="SerializesOrdersJobTypes">
/// Whether the channel's orders job types must never run concurrently on one connection.
/// </param>
public sealed record ChannelDescriptor(
    string Code,
    string DisplayName,
    IReadOnlySet<ChannelJobKind> SupportedJobKinds,
    bool SerializesOrdersJobTypes);
