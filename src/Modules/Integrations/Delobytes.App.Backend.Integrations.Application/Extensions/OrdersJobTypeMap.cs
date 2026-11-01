using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Domain.Enums;

namespace Delobytes.App.Backend.Integrations.Application.Extensions;

/// <summary>
/// The single source of truth for "which orders job types does a channel run". Both the seeder
/// (which creates a schedule) and the scheduler (which fires one) read it, so a channel can never
/// be seeded with one job type and dispatched as another.
/// </summary>
public static class OrdersJobTypeMap
{
    /// <summary>
    /// SystemChannelTemplate.Code of Wildberries connections, compared case-insensitively.
    /// </summary>
    public const string WildberriesChannelCode = "wildberries";

    /// <summary>
    /// SystemChannelTemplate.Code of Ozon connections, compared case-insensitively.
    /// </summary>
    public const string OzonChannelCode = "ozon";

    /// <summary>
    /// Returns the orders job types the channel runs, in scheduling order. An unknown or blank
    /// channel code yields an empty list: such a connection gets no orders schedule at all.
    /// </summary>
    /// <param name="channelCode">The connection's <c>SystemChannelTemplate.Code</c>.</param>
    /// <param name="enableOzonFbs">
    /// Whether the reserved Ozon FBS job type is enabled (<c>OzonImport:EnableFbs</c>).
    /// </param>
    public static IReadOnlyList<JobType> GetOrdersJobTypes(string? channelCode, bool enableOzonFbs)
    {
        if (string.IsNullOrWhiteSpace(channelCode))
        {
            return Array.Empty<JobType>();
        }

        if (IsWildberries(channelCode))
        {
            return new[] { JobType.OrdersImport };
        }

        if (IsOzon(channelCode))
        {
            return enableOzonFbs
                ? new[] { JobType.OrdersImportFbo, JobType.OrdersImportFbs }
                : new[] { JobType.OrdersImportFbo };
        }

        return Array.Empty<JobType>();
    }

    /// <summary>
    /// True when <paramref name="jobType"/> is an orders import type driven by a schedule.
    /// </summary>
    public static bool IsScheduledOrdersJobType(JobType jobType)
    {
        return jobType is JobType.OrdersImport or JobType.OrdersImportFbo or JobType.OrdersImportFbs;
    }

    /// <summary>
    /// Returns the rate-limit method the job type paces on the channel, or <c>null</c> when the
    /// pair is unsupported (and therefore needs no limiter pre-check).
    /// </summary>
    public static string? GetRateLimitMethod(string? channelCode, JobType jobType)
    {
        if (string.IsNullOrWhiteSpace(channelCode))
        {
            return null;
        }

        if (IsWildberries(channelCode))
        {
            return jobType == JobType.OrdersImport
                ? OrdersRateLimiterMethods.WildberriesSupplierOrders
                : null;
        }

        if (IsOzon(channelCode))
        {
            return jobType switch
            {
                JobType.OrdersImportFbo => OrdersRateLimiterMethods.OzonPostingFboList,
                JobType.OrdersImportFbs => OrdersRateLimiterMethods.OzonPostingFbsList,
                _ => null,
            };
        }

        return null;
    }

    private static bool IsWildberries(string channelCode)
    {
        return string.Equals(channelCode, WildberriesChannelCode, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOzon(string channelCode)
    {
        return string.Equals(channelCode, OzonChannelCode, StringComparison.OrdinalIgnoreCase);
    }
}
