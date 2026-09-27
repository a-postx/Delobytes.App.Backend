using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Delobytes.App.Backend.Integrations.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Services;

/// <summary>
/// Resolves API endpoint URLs for channel templates with in-memory caching.
/// </summary>
public class EndpointResolver : IEndpointResolver
{
    private readonly IntegrationsDbContext _context;
    private readonly IMemoryCache _cache;
    private readonly ILogger<EndpointResolver> _logger;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);

    /// <summary>
    /// Initializes a new instance of the <see cref="EndpointResolver"/> class.
    /// </summary>
    public EndpointResolver(
        IntegrationsDbContext context,
        IMemoryCache cache,
        ILogger<EndpointResolver> logger)
    {
        _context = context;
        _cache = cache;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<string> GetEndpointUrlAsync(
        Guid templateId,
        ChannelEndpointType endpointType,
        CancellationToken ct)
    {
        string cacheKey = $"endpoint:{templateId}:{endpointType}";

        if (_cache.TryGetValue<string>(cacheKey, out string? cachedUrl) && cachedUrl != null)
        {
            return cachedUrl;
        }

        SystemChannelEndpoint? endpoint = await _context.Set<SystemChannelEndpoint>()
            .AsNoTracking()
            .Where(e => e.SystemChannelTemplateId == templateId
                        && e.EndpointType == endpointType
                        && e.IsActive)
            .FirstOrDefaultAsync(ct);

        if (endpoint == null)
        {
            _logger.LogError(
                "Endpoint not found: TemplateId={TemplateId}, EndpointType={EndpointType}",
                templateId,
                endpointType);

            throw new InvalidOperationException(
                $"Endpoint '{endpointType}' not found or inactive for template '{templateId}'.");
        }

        _cache.Set(cacheKey, endpoint.BaseUrl, CacheDuration);

        return endpoint.BaseUrl;
    }
}
