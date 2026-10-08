using Delobytes.App.Backend.Catalog.Application.Commands.Products.ArchiveProduct;
using Delobytes.App.Backend.Catalog.Application.Commands.Products.CreateProduct;
using Delobytes.App.Backend.Catalog.Application.Commands.Products.RequestProductDeletion;
using Delobytes.App.Backend.Catalog.Application.Commands.Products.RestoreProduct;
using Delobytes.App.Backend.Catalog.Application.Commands.Products.UpdateProduct;
using Delobytes.App.Backend.Catalog.Application.Queries.Products;
using Delobytes.App.Backend.Catalog.Application.Queries.Products.GetProduct;
using Delobytes.App.Backend.Catalog.Application.Queries.Products.GetProductCost;
using Delobytes.App.Backend.Catalog.Application.Queries.Products.GetProductCostHistory;
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

    /// <summary>
    /// Returns products filtered by status, sorted and optionally paged.
    /// Omitted status means all statuses. Omitted page keeps the legacy behaviour: the full list
    /// is returned and no Skip/Take is applied. When page is supplied, pageSize defaults to 50 and
    /// is clamped to 1..200, sortBy must be one of name (default), sku, status, createdAt,
    /// updatedAt, and sortDir is asc (default) or desc. Sorting by updatedAt uses the creation
    /// moment for products that were never edited. Invalid sortBy, sortDir or page values fall
    /// back silently to their defaults instead of failing the request. Set includeCounts to also
    /// receive per-status totals for the filter tabs.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(GetProductsResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<GetProductsResponse>> GetAll(
        [FromQuery] ProductStatus? status,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDir,
        [FromQuery] bool? includeCounts,
        CancellationToken cancellationToken)
    {
        GetProductsResponse response = await _mediator.Send(
            new GetProductsQuery
            {
                Status = status,
                Page = page,
                PageSize = pageSize,
                SortBy = sortBy,
                SortDir = sortDir,
                IncludeCounts = includeCounts ?? false,
            },
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
                Barcodes = request.Barcodes,
                PackingUnit = request.PackingUnit,
            },
            cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = response.Id }, response);
    }

    /// <summary>
    /// Applies a partial update: fields omitted from the body (JSON null or absent) are left
    /// untouched, which lets a marketplace-linked product accept an SKU change while every other
    /// field keeps being driven by the import. An empty SKU is rejected with 422, and a SKU
    /// already taken within the tenant with 409 catalog.product.sku_conflict.
    /// </summary>
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
                Sku = request.Sku,
                Name = request.Name,
                Description = request.Description,
                Barcodes = request.Barcodes,
                PackingUnit = request.PackingUnit,
            },
            cancellationToken);

        if (!response.Found)
        {
            return NotFound();
        }

        return NoContent();
    }

    [HttpGet("{id:guid}/cost/history")]
    public async Task<ActionResult<GetProductCostHistoryResponse>> GetCostHistory(
        Guid id,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken cancellationToken = default)
    {
        GetProductCostHistoryResponse response = await _mediator.Send(
            new GetProductCostHistoryQuery { ProductId = id, Skip = skip, Take = take },
            cancellationToken);

        if (!response.Found)
        {
            return NotFound();
        }

        return Ok(response);
    }

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

/// <summary>
/// Body of a product creation. SKU and name are mandatory, as the entity requires both.
/// </summary>
public class CreateProductRequest
{
    public string Sku { get; set; } = default!;

    public string Name { get; set; } = default!;

    public string? Description { get; set; }

    public List<ProductBarcodeDto>? Barcodes { get; set; }

    public PackingUnitDto? PackingUnit { get; set; }
}

/// <summary>
/// Body of a partial product update. Every property is optional and a null means "leave as is":
/// an omitted SKU is not an empty SKU, and an omitted Name is not a request to blank the product.
/// </summary>
public class UpdateProductRequest
{
    public string? Sku { get; set; }

    public string? Name { get; set; }

    public string? Description { get; set; }

    public List<ProductBarcodeDto>? Barcodes { get; set; }

    public PackingUnitDto? PackingUnit { get; set; }
}
