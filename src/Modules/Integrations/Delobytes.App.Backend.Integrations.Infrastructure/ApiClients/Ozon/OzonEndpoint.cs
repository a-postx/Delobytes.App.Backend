using Delobytes.App.Backend.Integrations.Domain.Entities;

namespace Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Ozon;

/// <summary>
/// The system channel template the client serves. Shared by the facade and every capability client,
/// so "not bound yet" is one guard instead of one per method group.
/// </summary>
internal sealed class OzonEndpoint
{
    private const string DefaultApiBaseUrl = "https://api-seller.ozon.ru";

    /// <summary>
    /// Gets the identifier of the bound template, or null when the client was not obtained through the
    /// factory.
    /// </summary>
    public Guid? TemplateId { get; private set; }

    private string? ApiBaseUrl { get; set; }

    /// <summary>
    /// Gets the bound base URL, or Ozon's public one when the client was not obtained through the
    /// factory. Credential validation runs before a connection exists and may be called unbound.
    /// </summary>
    public string BaseUrl => ApiBaseUrl ?? DefaultApiBaseUrl;

    public void Bind(SystemChannelTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);

        TemplateId = template.Id;
        ApiBaseUrl = string.IsNullOrWhiteSpace(template.ApiBaseUrl)
            ? DefaultApiBaseUrl
            : template.ApiBaseUrl;
    }

    /// <summary>
    /// Returns the bound base URL, refusing to fall back: a data call made by an unbound client would
    /// silently hit the public endpoint instead of the configured one.
    /// </summary>
    public string RequireBaseUrl()
    {
        if (TemplateId is null)
        {
            throw new InvalidOperationException(
                "Template must be bound before calling API methods. Obtain the client from IChannelApiClientFactory.");
        }

        return BaseUrl;
    }
}
