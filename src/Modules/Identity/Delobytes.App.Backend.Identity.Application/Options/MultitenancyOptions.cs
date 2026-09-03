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
}
