using System.Diagnostics.CodeAnalysis;
using Delobytes.App.Backend.Integrations.Application.Jobs;

namespace Delobytes.App.Backend.Integrations.Application.SalesChannels;

/// <summary>
/// The channels this system integrates with, described by code, name, supported import kinds and
/// scheduling policy. Concrete job types are owned by each channel module.
/// </summary>
public static class ChannelCatalog
{
    public const string WildberriesCode = "wildberries";

    public const string OzonCode = "ozon";

    public static IReadOnlyList<ChannelDescriptor> All { get; } = new[]
    {
        new ChannelDescriptor(
            WildberriesCode,
            "Wildberries",
            new HashSet<ChannelJobKind> { ChannelJobKind.ProductsImport, ChannelJobKind.OrdersImport },
            SerializesOrdersJobTypes: false),

        // Both Ozon schemas are always scheduled: they share one connection and one rate limiter, so
        // the scheduler serialises them rather than a configuration flag deciding which exists.
        new ChannelDescriptor(
            OzonCode,
            "Ozon",
            new HashSet<ChannelJobKind> { ChannelJobKind.OrdersImport },
            SerializesOrdersJobTypes: true),
    };

    public static bool TryGet(string? code, [NotNullWhen(true)] out ChannelDescriptor descriptor)
    {
        foreach (ChannelDescriptor candidate in All)
        {
            if (string.Equals(candidate.Code, code, StringComparison.OrdinalIgnoreCase))
            {
                descriptor = candidate;
                return true;
            }
        }

        descriptor = null!;
        return false;
    }
}
