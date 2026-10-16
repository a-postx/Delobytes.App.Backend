namespace Delobytes.App.Backend.Identity.Application.Options;

/// <summary>
/// Configuration options for multi-tenancy features.
/// </summary>
public class MultitenancyOptions
{
    /// <summary>
    /// Gets or sets the maximum number of tenants a single user can belong to.
    /// Default is 5.
    /// </summary>
    public int MaxTenantsPerUser { get; set; } = 2;

    /// <summary>
    /// Gets or sets the default value assigned to Tenant.MaxBackgroundJobsParallelism when a
    /// new tenant is created. Not editable by the tenant itself (decision: parallelism is an
    /// operational setting, not a user-facing one).
    /// </summary>
    public int DefaultMaxBackgroundJobsParallelism { get; set; } = 1;
}
