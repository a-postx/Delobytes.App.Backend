using System;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Catalog.Application.Commands.Products.UpdateProduct;
using Delobytes.App.Backend.Controllers;
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
/// value the form sent was silently discarded and the user was shown "Товар обновлён" over an
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

    private void SetupResponse(bool found)
    {
        _mediator
            .Setup(m => m.Send(It.IsAny<UpdateProductCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpdateProductResponse { Found = found });
    }

    [Fact]
    public async Task Update_MapsSkuFromRequestBodyOntoCommand()
    {
        // Arrange
        Guid productId = Guid.NewGuid();
        SetupResponse(found: true);

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
        SetupResponse(found: true);

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
        SetupResponse(found: true);

        // Act
        ActionResult result = await BuildController().Update(
            Guid.NewGuid(),
            new UpdateProductRequest { Sku = "NEW-SKU" },
            CancellationToken.None);

        // Assert
        result.Should().BeOfType<NoContentResult>();
    }

    [Fact]
    public async Task Update_NotFound_Returns404()
    {
        // Arrange
        SetupResponse(found: false);

        // Act
        ActionResult result = await BuildController().Update(
            Guid.NewGuid(),
            new UpdateProductRequest { Sku = "NEW-SKU" },
            CancellationToken.None);

        // Assert
        result.Should().BeOfType<NotFoundResult>();
    }
}
