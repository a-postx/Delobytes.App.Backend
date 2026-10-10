using Delobytes.App.Backend.Catalog.Application.Commands.ProductWorkRates.CreateProductWorkRate;
using Delobytes.App.Backend.Catalog.Application.Commands.ProductWorkRates.DeleteProductWorkRate;
using Delobytes.App.Backend.Catalog.Application.Commands.ProductWorkRates.UpdateProductWorkRate;
using Delobytes.App.Backend.Catalog.Application.Queries.ProductWorkRates.GetAllProductWorkRates;
using Delobytes.App.Backend.Catalog.Application.Queries.ProductWorkRates.GetProductWorkRates;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Delobytes.App.Backend.Controllers;

/// <summary>
/// Endpoints for the Product Work Rates catalog.
/// Creating a new rate (POST) deactivates the previously active version and appends a new one;
/// an existing, still-active version can also be corrected in place (PUT), e.g. to fix a typo.
/// Deletion is a soft-delete: IsActive is set to false, the record is retained for historical accuracy.
/// </summary>
[ApiController]
[Route("api/catalogs/product-work-rates")]
[Authorize]
public class ProductWorkRatesController : ControllerBase
{
    private readonly IMediator _mediator;

    public ProductWorkRatesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Returns the work rate list as a page of products, each with all of its versions.
    /// An omitted search keeps the full list; when supplied it is trimmed, capped at 200 characters
    /// and matched case-insensitively as a substring of the product name or SKU (a match in either
    /// field is enough). The status filter is applied per product, not per version: active means the
    /// product has at least one active version, inactive that it has at least one superseded or
    /// removed one. Be aware that the source is the work rate table, so "All" returns every product
    /// that has at least one version, not every product in the catalog — a product without a rate
    /// cannot be returned here. An omitted page keeps the legacy behaviour: the whole list is
    /// returned and no Skip/Take is applied. When page is supplied, pageSize defaults to 25 and is
    /// clamped to 1..200, sortBy must be one of productName (default), updatedAt, validFrom, and
    /// sortDir is asc (default) or desc; the date keys describe the latest version of each product.
    /// Invalid sortBy, sortDir or page values fall back silently to their defaults instead of failing
    /// the request. Set includeCounts to receive per-group totals for the filter labels, counted over
    /// the searched result set and per product, not per version.
    /// Paging counts products: a product with several versions always arrives whole on one page, so
    /// totalCount is the number of matching products, not the number of rows in the response.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(GetAllProductWorkRatesResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<GetAllProductWorkRatesResponse>> GetAll(
        [FromQuery] string? search,
        [FromQuery] ProductWorkRateGroupFilter? status,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDir,
        [FromQuery] bool? includeCounts,
        CancellationToken cancellationToken)
    {
        GetAllProductWorkRatesResponse response = await _mediator.Send(
            new GetAllProductWorkRatesQuery
            {
                Search = search,
                Status = status ?? ProductWorkRateGroupFilter.Active,
                Page = page,
                PageSize = pageSize,
                SortBy = sortBy,
                SortDir = sortDir,
                IncludeCounts = includeCounts ?? false,
            },
            cancellationToken);

        return Ok(response);
    }

    /// <summary>
    /// Returns the version history of work rates for a given product, newest version first.
    /// Each item carries the product name and SKU, so the caller does not need a separate lookup.
    /// </summary>
    [HttpGet("by-product/{productId:guid}")]
    public async Task<ActionResult<GetProductWorkRatesResponse>> GetByProduct(
        Guid productId,
        CancellationToken cancellationToken)
    {
        GetProductWorkRatesResponse response = await _mediator.Send(
            new GetProductWorkRatesQuery { ProductId = productId },
            cancellationToken);

        return Ok(response);
    }

    /// <summary>
    /// Appends a new work rate version for a product and deactivates the version it supersedes.
    /// Requires Manager or Administrator role.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<CreateProductWorkRateResponse>> Create(
        [FromBody] CreateProductWorkRateApiRequest request,
        CancellationToken cancellationToken)
    {
        CreateProductWorkRateResponse response = await _mediator.Send(
            new CreateProductWorkRateCommand
            {
                ProductId = request.ProductId,
                WorkRateId = request.WorkRateId,
                AssemblyRatePerDay = request.AssemblyRatePerDay,
                ValidFrom = request.ValidFrom,
            },
            cancellationToken);

        return CreatedAtAction(
            nameof(GetByProduct),
            new { productId = request.ProductId },
            response);
    }

    /// <summary>
    /// Corrects an existing, still-active work rate version in place (e.g. a typo in the rate or
    /// the date). Does not change version history. Requires Manager or Administrator role.
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateProductWorkRateApiRequest request,
        CancellationToken cancellationToken)
    {
        UpdateProductWorkRateResponse response = await _mediator.Send(
            new UpdateProductWorkRateCommand
            {
                Id = id,
                WorkRateId = request.WorkRateId,
                AssemblyRatePerDay = request.AssemblyRatePerDay,
                ValidFrom = request.ValidFrom,
            },
            cancellationToken);

        if (!response.Found)
        {
            return NotFound();
        }

        return NoContent();
    }

    /// <summary>
    /// Soft-deletes a rate record (IsActive = false).
    /// The record is retained so historical margin calculation snapshots remain valid.
    /// Requires Manager or Administrator role.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        DeleteProductWorkRateResponse response = await _mediator.Send(
            new DeleteProductWorkRateCommand { Id = id },
            cancellationToken);

        if (!response.Found)
        {
            return NotFound();
        }

        return NoContent();
    }
}

public class CreateProductWorkRateApiRequest
{
    public Guid ProductId { get; set; }

    public Guid WorkRateId { get; set; }

    public int AssemblyRatePerDay { get; set; }

    public DateOnly ValidFrom { get; set; }
}

public class UpdateProductWorkRateApiRequest
{
    public Guid WorkRateId { get; set; }

    public int AssemblyRatePerDay { get; set; }

    public DateOnly ValidFrom { get; set; }
}
