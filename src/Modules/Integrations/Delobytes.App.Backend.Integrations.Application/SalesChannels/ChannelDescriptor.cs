using Delobytes.App.Backend.Integrations.Domain.Enums;

namespace Delobytes.App.Backend.Integrations.Application.SalesChannels;

/// <summary>
/// Everything the application layer needs to know about one marketplace channel. The orders job
/// types are in scheduling order, and <see cref="SerializesOrdersJobTypes"/> tells the scheduler
/// whether those job types must never run concurrently on one connection.
/// </summary>
public sealed record ChannelDescriptor(
    string Code,
    string DisplayName,
    IReadOnlyList<JobType> OrdersJobTypes,
    bool SerializesOrdersJobTypes);
