namespace Delobytes.App.Backend.Sales.Domain.Enums;

/// <summary>
/// Provenance of a monetary value: where the number came from, said explicitly so a consumer can
/// distinguish a channel-reported figure from a configured or derived one.
/// </summary>
public enum ValueSource
{
    /// <summary>
    /// The channel does not provide this value, or it has not arrived yet.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// Reported inside the order payload itself.
    /// </summary>
    ChannelOrderApi = 1,

    /// <summary>
    /// Arrived later, from a separate financial report.
    /// </summary>
    ChannelFinanceReport = 2,

    /// <summary>
    /// Taken from tenant or channel configuration rather than reported by the channel.
    /// </summary>
    TenantConfigured = 3,

    /// <summary>
    /// Entered by a user.
    /// </summary>
    UserEntered = 4,

    /// <summary>
    /// Computed from other values.
    /// </summary>
    Derived = 5,
}
