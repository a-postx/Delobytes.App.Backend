using Delobytes.App.Backend.Catalog.Application.Products;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using MassTransit;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Messaging.Consumers;

/// <summary>
/// MassTransit adapter for <see cref="ProductBatchImporter"/>.
/// </summary>
public class ProductBatchImportConsumer : IConsumer<ProductBatchImportRequestedEvent>
{
    private readonly ProductBatchImporter _importer;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProductBatchImportConsumer"/> class.
    /// </summary>
    /// <param name="importer">Application-service that imports the batch.</param>
    public ProductBatchImportConsumer(ProductBatchImporter importer)
    {
        _importer = importer;
    }

    /// <inheritdoc/>
    public async Task Consume(ConsumeContext<ProductBatchImportRequestedEvent> context)
    {
        await _importer.ImportAsync(context.Message, context.CancellationToken);
    }
}
