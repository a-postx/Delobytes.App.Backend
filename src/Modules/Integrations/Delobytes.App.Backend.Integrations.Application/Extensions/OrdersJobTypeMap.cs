using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Application.SalesChannels;
using Delobytes.App.Backend.Integrations.Domain.Enums;

namespace Delobytes.App.Backend.Integrations.Application.Extensions;

/// <summary>
/// Compatibility facade over <see cref="ChannelCatalog"/> for callers that already depend on this
/// static (the seeder and the scheduler). The catalogue is the single source of truth; this class
/// only restates its answers in the shapes those callers use.
/// </summary>
public static class OrdersJobTypeMap
{
    /// <summary>
    /// Returns the orders job types the channel runs, in scheduling order. An unknown or blank
    /// channel code yields an empty list: such a connection gets no orders schedule at all.
    /// </summary>
    public static IReadOnlyList<JobType> GetOrdersJobTypes(string? channelCode)
    {
        return ChannelCatalog.TryGet(channelCode, out ChannelDescriptor descriptor)
            ? descriptor.OrdersJobTypes
            : Array.Empty<JobType>();
    }

    /// <summary>
    /// True when <paramref name="jobType"/> is an orders import type driven by a schedule.
    /// </summary>
    public static bool IsScheduledOrdersJobType(JobType jobType)
    {
        foreach (ChannelDescriptor descriptor in ChannelCatalog.All)
        {
            if (descriptor.OrdersJobTypes.Contains(jobType))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns the rate-limit method the job type paces on the channel, or <c>null</c> when the
    /// pair is unsupported (and therefore needs no limiter pre-check). This is a compatibility
    /// facade for <c>SchedulerHostedService</c>; API clients should reference
    /// <see cref="WildberriesApiMethodRegistry"/> or <see cref="OrdersRateLimiterMethods"/> as
    /// appropriate.
    /// </summary>
    public static string? GetRateLimitMethod(string? channelCode, JobType jobType)
    {
        if (!ChannelCatalog.TryGet(channelCode, out ChannelDescriptor descriptor)
            || !descriptor.OrdersJobTypes.Contains(jobType))
        {
            return null;
        }

        return jobType switch
        {
            JobType.WbOrdersImport => WildberriesApiMethodRegistry.SupplierOrders,
            JobType.OzonOrdersImportFbo => OrdersRateLimiterMethods.OzonPostingFboList,
            JobType.OzonOrdersImportFbs => OrdersRateLimiterMethods.OzonPostingFbsList,
            _ => null,
        };
    }
}
