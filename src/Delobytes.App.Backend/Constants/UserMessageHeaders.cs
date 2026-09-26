namespace Delobytes.App.Backend.Constants;

/// <summary>
/// Constants for user-related message headers in MassTransit messages.
/// </summary>
public static class UserMessageHeaders
{
    /// <summary>
    /// Header key for the user identifier.
    /// </summary>
    public const string UserId = "X-User-Id";
}
