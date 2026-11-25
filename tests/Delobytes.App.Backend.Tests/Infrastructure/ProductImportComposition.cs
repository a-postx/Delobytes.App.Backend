using System;
using Delobytes.App.Backend.Catalog.Application.Interfaces;
using Delobytes.App.Backend.Catalog.Application.Numbering;
using Delobytes.App.Backend.Catalog.Application.Products;
using Delobytes.App.Backend.Catalog.Infrastructure.Messaging;
using Delobytes.App.Backend.Tests.Application.Catalog.Numbering;
using MassTransit;
using Microsoft.Extensions.Logging;
using Moq;

namespace Delobytes.App.Backend.Tests.Infrastructure;

/// <summary>
/// Wires a <see cref="ProductBatchImporter"/> and its collaborators over the given store. Tests
/// that need a non-default numbering scheme or clock pass their own; the rest get the fakes the
/// import tests use by default.
/// </summary>
internal static class ProductImportComposition
{
    internal static ProductBatchImporter Build(
        IProductImportStore store,
        IProductPhotoService photoService,
        IPublishEndpoint publishEndpoint,
        INumberingSchemeProvider? numberingSchemes = null,
        INumberAllocator? numberAllocator = null,
        TimeProvider? timeProvider = null)
    {
        CardBarcodePolicy barcodes = new CardBarcodePolicy(store, Mock.Of<ILogger<CardBarcodePolicy>>());

        return new ProductBatchImporter(
            store,
            new ProductCardMatcher(store, Mock.Of<ILogger<ProductCardMatcher>>()),
            new ChannelProductUpserter(store, barcodes),
            barcodes,
            new ProductCodeResolver(
                store,
                numberingSchemes ?? new FakeNumberingSchemeProvider(),
                numberAllocator ?? new FakeNumberAllocator(),
                Mock.Of<ILogger<ProductCodeResolver>>()),
            new ProductImportEffectsPublisher(
                store,
                photoService,
                new MassTransitEventPublisher(publishEndpoint),
                Mock.Of<ILogger<ProductImportEffectsPublisher>>()),
            new MassTransitEventPublisher(publishEndpoint),
            Mock.Of<ILogger<ProductBatchImporter>>(),
            timeProvider ?? TimeProvider.System);
    }
}
