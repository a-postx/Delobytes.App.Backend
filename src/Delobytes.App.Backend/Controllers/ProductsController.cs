using Delobytes.App.Backend.Catalog.Application.Commands.Products.ArchiveProduct;
using Delobytes.App.Backend.Catalog.Application.Commands.Products.CreateProduct;
using Delobytes.App.Backend.Catalog.Application.Commands.Products.RequestProductDeletion;
using Delobytes.App.Backend.Catalog.Application.Commands.Products.RestoreProduct;
using Delobytes.App.Backend.Catalog.Application.Commands.Products.UpdateProduct;
using Delobytes.App.Backend.Catalog.Application.Queries.Products.GetProduct;
using Delobytes.App.Backend.Catalog.Application.Queries.Products.GetProductDeletionStatus;
using Delobytes.App.Backend.Catalog.Application.Queries.Products.GetProducts;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Delobytes.App.Backend.Controllers;

/// <summary>
/// CRUD and lifecycle endpoints for Products.
/// DELETE is asynchronous: returns 202 Accepted; clients poll GET {id}/deletion-status for progress.
/// Two creation paths are planned: manual (this API) and marketplace import (not yet implemented).
/// </summary>
[ApiController]
[Route("api/catalogs/products")]
[Authorize]
public class ProductsController : ControllerBase
{
    private readonly IMediator _mediator;

    public ProductsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>Returns products filtered by status. When status is omitted, products of all statuses are returned.</summary>
    [HttpGet]
    public async Task<ActionResult<GetProductsResponse>> GetAll(
        [FromQuery] ProductStatus? status,
        CancellationToken cancellationToken)
    {
        GetProductsResponse response = await _mediator.Send(
            new GetProductsQuery { Status = status },
            cancellationToken);

        return Ok(response);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GetProductResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        GetProductResponse response = await _mediator.Send(
            new GetProductQuery { Id = id },
            cancellationToken);

        if (!response.Found)
        {
            return NotFound();
        }

        return Ok(response);
    }

    [HttpPost]
    public async Task<ActionResult<CreateProductResponse>> Create(
        [FromBody] CreateProductRequest request,
        CancellationToken cancellationToken)
    {
        CreateProductResponse response = await _mediator.Send(
            new CreateProductCommand
            {
                Sku = request.Sku,
                Name = request.Name,
                Description = request.Description,
            },
            cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = response.Id }, response);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult> Update(
        Guid id,
        [FromBody] UpdateProductRequest request,
        CancellationToken cancellationToken)
    {
        UpdateProductResponse response = await _mediator.Send(
            new UpdateProductCommand
            {
                Id = id,
                Name = request.Name,
                Description = request.Description,
            },
            cancellationToken);

        if (!response.Found)
        {
            return NotFound();
        }

        return NoContent();
    }

    /// <summary>Archives a product. Synchronous. Product is hidden but data is preserved.</summary>
    [HttpPost("{id:guid}/archive")]
    public async Task<ActionResult> Archive(Guid id, CancellationToken cancellationToken)
    {
        ArchiveProductResponse response = await _mediator.Send(
            new ArchiveProductCommand { ProductId = id },
            cancellationToken);

        if (!response.Found)
        {
            return NotFound();
        }

        return NoContent();
    }

    /// <summary>Restores an Archived or DeletionFailed product to Active. Synchronous.</summary>
    [HttpPost("{id:guid}/restore")]
    public async Task<ActionResult> Restore(Guid id, CancellationToken cancellationToken)
    {
        RestoreProductResponse response = await _mediator.Send(
            new RestoreProductCommand { ProductId = id },
            cancellationToken);

        if (!response.Found)
        {
            return NotFound();
        }

        if (!response.Accepted)
        {
            return Conflict(new { message = "Only Archived or DeletionFailed products can be restored." });
        }

        return NoContent();
    }

    /// <summary>
    /// Initiates asynchronous product deletion.
    /// Returns 202 Accepted immediately. Clients should poll GET {id}/deletion-status
    /// to monitor progress (recommended: exponential backoff 1s → 2s → 3s → 5s → 10s).
    /// Products with linked orders are denied deletion (status becomes DeletionFailed).
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> RequestDeletion(Guid id, CancellationToken cancellationToken)
    {
        RequestProductDeletionResponse response = await _mediator.Send(
            new RequestProductDeletionCommand { ProductId = id },
            cancellationToken);

        if (!response.Found)
        {
            return NotFound();
        }

        if (!response.Accepted)
        {
            return Conflict(new { message = response.ErrorMessage });
        }

        return Accepted(new { productId = response.ProductId });
    }

    /// <summary>
    /// Returns the current deletion status for a product.
    /// Poll this endpoint after calling DELETE to monitor progress.
    /// </summary>
    [HttpGet("{id:guid}/deletion-status")]
    public async Task<ActionResult<GetProductDeletionStatusResponse>> GetDeletionStatus(
        Guid id,
        CancellationToken cancellationToken)
    {
        GetProductDeletionStatusResponse response = await _mediator.Send(
            new GetProductDeletionStatusQuery { ProductId = id },
            cancellationToken);

        if (!response.Found)
        {
            return NotFound();
        }

        return Ok(response);
    }
}

public class CreateProductRequest
{
    public string Sku { get; set; } = default!;

    public string Name { get; set; } = default!;

    public string? Description { get; set; }
}

public class UpdateProductRequest
{
    public string Name { get; set; } = default!;

    public string? Description { get; set; }
}
