using System;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Catalog.Api.Controllers;
using Delobytes.App.Backend.Catalog.Application.Commands.Products;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Controllers;

/// <summary>
/// Tests for the PUT api/catalogs/products/{id} endpoint.
///
/// The endpoint is the seam where the SKU used to be dropped: the command never carried it, so the
/// value the form sent was silently discarded and the user was shown "РўРѕРІР°СЂ РѕР±РЅРѕРІР»С‘РЅ" over an
/// unchanged SKU. These tests pin the mapping from the request body to the command.
/// </summary>
public class ProductsControllerUpdateTests
{
    private readonly Mock<IMediator> _mediator;

    public ProductsControllerUpdateTests()
    {
        _mediator = new Mock<IMediator>();
    }

    private ProductsController BuildController() => new ProductsController(_mediator.Object);

    private void SetupResponse()
    {
        _mediator
            .Setup(m => m.Send(It.IsAny<UpdateProductCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpdateProductResponse());
    }

    [Fact]
    public async Task Update_MapsSkuFromRequestBodyOntoCommand()
    {
        // Arrange
        Guid productId = Guid.NewGuid();
        SetupResponse();

        UpdateProductRequest request = new UpdateProductRequest
        {
            Sku = "NEW-SKU",
        };

        // Act
        await BuildController().Update(productId, request, CancellationToken.None);

        // Assert
        _mediator.Verify(
            m => m.Send(
                It.Is<UpdateProductCommand>(c => c.Id == productId && c.Sku == "NEW-SKU"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Update_SkuOnlyRequest_SendsEveryOtherFieldAsNull()
    {
        // Arrange: null is the wire-level signal for "leave this field alone". If the controller
        // turned an absent property into an empty string or an empty list, an SKU-only save would
        // start blanking the product instead of being partial.
        Guid productId = Guid.NewGuid();
        SetupResponse();

        UpdateProductRequest request = new UpdateProductRequest
        {
            Sku = "NEW-SKU",
        };

        // Act
        await BuildController().Update(productId, request, CancellationToken.None);

        // Assert
        _mediator.Verify(
            m => m.Send(
                It.Is<UpdateProductCommand>(c =>
                    c.Name == null &&
                    c.Description == null &&
                    c.Barcodes == null &&
                    c.PackingUnit == null),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Update_Found_Returns204NoContent()
    {
        // Arrange
        SetupResponse();

        // Act
        ActionResult result = await BuildController().Update(
            Guid.NewGuid(),
            new UpdateProductRequest { Sku = "NEW-SKU" },
            CancellationToken.None);

        // Assert
        result.Should().BeOfType<NoContentResult>();
    }
}

