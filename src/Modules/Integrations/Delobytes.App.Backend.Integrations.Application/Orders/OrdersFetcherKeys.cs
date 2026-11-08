using Delobytes.App.Backend.Integrations.Domain.Enums;

namespace Delobytes.App.Backend.Integrations.Application.Orders;

/// <summary>
/// The keyed-service key of one (channel code, orders job type) pair, and of the parts that pair
/// owns. The job type is rendered by its enum name, which is stable across renames of the public
/// API token.
/// </summary>
internal static class OrdersFetcherKeys
{
    public const string BackfillSuffix = "backfill";

    public const string DeltaSuffix = "delta";

    public const string ReaderSuffix = "reader";

    public static string Fetchers(string channelCode, JobType jobType)
    {
        return $"{channelCode}:{jobType}";
    }

    public static string Backfill(string channelCode, JobType jobType)
    {
        return $"{Fetchers(channelCode, jobType)}:{BackfillSuffix}";
    }

    public static string Delta(string channelCode, JobType jobType)
    {
        return $"{Fetchers(channelCode, jobType)}:{DeltaSuffix}";
    }

    public static string Reader(string channelCode, JobType jobType, string role)
    {
        return $"{Fetchers(channelCode, jobType)}:{ReaderSuffix}:{role}";
    }
}
