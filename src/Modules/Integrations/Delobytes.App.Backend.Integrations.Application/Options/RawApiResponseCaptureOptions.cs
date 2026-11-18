namespace Delobytes.App.Backend.Integrations.Application.Options;

/// <summary>
/// Bound on the verbatim payload the audit handler stores per attempt. A finance report page can be
/// tens of megabytes, and the host runs with 1 GB of memory.
/// </summary>
public sealed class RawApiResponseCaptureOptions
{
    /// <summary>Default cap: 4 MiB.</summary>
    public const int DefaultMaxPayloadBytes = 4 * 1024 * 1024;

    /// <summary>
    /// Gets or sets the maximum number of characters stored per payload. A larger payload is stored
    /// as its prefix plus a truncation marker.
    /// </summary>
    public int MaxPayloadBytes { get; set; } = DefaultMaxPayloadBytes;
}
