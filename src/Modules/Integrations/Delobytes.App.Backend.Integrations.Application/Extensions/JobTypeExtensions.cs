using Delobytes.App.Backend.Integrations.Domain.Enums;

namespace Delobytes.App.Backend.Integrations.Application.Extensions;

/// <summary>
/// Maps <see cref="JobType"/> to and from the camelCase-cased type token used by the unified
/// jobs API ("products-import", "orders-import"). The enum names themselves are not a stable
/// API contract: renaming a member must not silently change the public route segment.
/// </summary>
public static class JobTypeExtensions
{
    /// <summary>
    /// API type token for <see cref="JobType.ProductsImport"/>.
    /// </summary>
    public const string ProductsImport = "products-import";

    /// <summary>
    /// API type token for <see cref="JobType.OrdersImport"/>.
    /// </summary>
    public const string OrdersImport = "orders-import";

    /// <summary>
    /// API type token for <see cref="JobType.OrdersSync"/>.
    /// </summary>
    public const string OrdersSync = "orders-sync";

    /// <summary>
    /// Returns the API type token for the given job type.
    /// </summary>
    public static string ToStringCamelCase(this JobType jobType)
    {
        return jobType switch
        {
            JobType.ProductsImport => ProductsImport,
            JobType.OrdersImport => OrdersImport,
            JobType.OrdersSync => OrdersSync,
            _ => throw new ArgumentOutOfRangeException(
                nameof(jobType),
                jobType,
                "Unknown job type."),
        };
    }

    /// <summary>
    /// Resolves an API type token to a job type. Returns null for null, blank or unknown tokens;
    /// matching is case-insensitive so that "Products-Import" is accepted.
    /// </summary>
    public static JobType? FromString(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            return null;
        }

        string normalized = type.Trim();

        foreach (JobType candidate in Enum.GetValues<JobType>())
        {
            if (string.Equals(candidate.ToStringCamelCase(), normalized, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// Try-pattern counterpart of <see cref="FromString"/>.
    /// </summary>
    public static bool TryFromString(string? type, out JobType jobType)
    {
        JobType? resolved = FromString(type);

        jobType = resolved ?? default;

        return resolved.HasValue;
    }

    /// <summary>
    /// Cancellation is offered only for user-initiated job types. Orders import is driven by the
    /// scheduler: cancelling it mid-run would break watermark continuity, so the API must reject it.
    /// </summary>
    public static bool IsCancellable(this JobType jobType)
    {
        return jobType == JobType.ProductsImport;
    }

    /// <summary>
    /// True when the job type may be started through the API. Orders import is scheduler-only.
    /// </summary>
    public static bool IsManuallyStartable(this JobType jobType)
    {
        return jobType == JobType.ProductsImport;
    }
}
