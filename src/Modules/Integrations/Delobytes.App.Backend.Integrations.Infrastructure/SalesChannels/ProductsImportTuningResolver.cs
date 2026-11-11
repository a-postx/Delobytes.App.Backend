using Delobytes.App.Backend.Integrations.Application.Products;
using Microsoft.Extensions.DependencyInjection;

namespace Delobytes.App.Backend.Integrations.Infrastructure.SalesChannels;

/// <summary>
/// Resolves the products import tuning each channel module registered, so callers pass a channel
/// code instead of depending on that channel's options type.
/// </summary>
public sealed class ProductsImportTuningResolver : IProductsImportTuningResolver
{
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProductsImportTuningResolver"/> class.
    /// </summary>
    /// <param name="serviceProvider">Service provider holding the channels' keyed tunings.</param>
    public ProductsImportTuningResolver(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    /// <inheritdoc/>
    public ProductsImportTuning Resolve(string channelCode)
    {
        ProductsImportTuning? tuning = _serviceProvider
            .GetKeyedService<ProductsImportTuning>(channelCode);

        return tuning ?? ProductsImportTuning.Default;
    }
}
