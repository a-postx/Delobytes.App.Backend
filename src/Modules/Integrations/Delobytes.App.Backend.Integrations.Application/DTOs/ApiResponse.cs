namespace Delobytes.App.Backend.Integrations.Application.DTOs;

/// <summary>
/// Represents a generic API response wrapper.
/// </summary>
/// <typeparam name="T">The type of data contained in the response.</typeparam>
public class ApiResponse<T>
{
    /// <summary>
    /// Gets or sets a value indicating whether the API call was successful.
    /// </summary>
    public bool IsSuccess { get; set; }

    /// <summary>
    /// Gets or sets the response data.
    /// </summary>
    public T? Data { get; set; }

    /// <summary>
    /// Gets or sets the error message if the call failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Gets or sets the HTTP status code.
    /// </summary>
    public int? StatusCode { get; set; }

    /// <summary>
    /// Gets or sets the date and time when the response was received.
    /// </summary>
    public DateTimeOffset Timestamp { get; set; }
}
