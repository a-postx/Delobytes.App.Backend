namespace Delobytes.App.Backend.Integrations.Contracts.Models;

/// <summary>
/// Codes of the sales channels this system integrates with, shared by producers and consumers of
/// channel-scoped contracts so neither has to hardcode marketplace names.
/// </summary>
public static class ChannelCodes
{
    public const string Wildberries = "wildberries";

    public const string Ozon = "ozon";

    public const string YandexKit = "yandex.kit";
}
