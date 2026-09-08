namespace Delobytes.App.Backend.Integrations.Application.Models;

/// <summary>
/// Represents account/shop identity data returned by a marketplace API.
/// All fields are optional because not every marketplace provides all three.
/// </summary>
public class AccountInfo
{
    public string? CustomerName { get; init; }
    public string? LegalName { get; init; }
    public string? Inn { get; init; }
}
