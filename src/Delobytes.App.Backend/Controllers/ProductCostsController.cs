using Delobytes.App.Backend.Catalog.Application.Queries.Products.GetProductCost;
using Delobytes.App.Backend.Catalog.Application.Queries.Products.GetProductCostsBatch;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Delobytes.App.Backend.Controllers;

/// <summary>
/// Cost calculation endpoints for products.
/// Provides both single-product detailed breakdown and batch calculation for table views.
/// </summary>
[ApiController]
[Route("api/catalogs/product-costs")]
[Authorize]
public class ProductCostsController : ControllerBase
{
    private readonly IMediator _mediator;

    public ProductCostsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Returns the cost breakdown of multiple products in a single request.
    /// Designed for table views where only summary totals are needed (no line-by-line detail).
    /// </summary>
    /// <param name="productIds">Comma-separated list of product GUIDs (max 100).</param>
    /// <param name="asOf">Optional date in yyyy-MM-dd format; defaults to today.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpGet]
    public async Task<ActionResult<GetProductCostsBatchResponse>> GetBatch(
        [FromQuery] string productIds,
        [FromQuery] DateOnly? asOf,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(productIds))
        {
            return BadRequest(new { message = "productIds query parameter is required." });
        }

        string[] parts = productIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length == 0)
        {
            return BadRequest(new { message = "productIds cannot be empty." });
        }

        if (parts.Length > 100)
        {
            return BadRequest(new { message = "Maximum 100 product IDs allowed per request." });
        }

        List<Guid> parsedIds = new List<Guid>();

        foreach (string part in parts)
        {
            if (!Guid.TryParse(part, out Guid guid))
            {
                return BadRequest(new { message = $"Invalid GUID format: {part}" });
            }

            parsedIds.Add(guid);
        }

        GetProductCostsBatchResponse response = await _mediator.Send(
            new GetProductCostsBatchQuery
            {
                ProductIds = parsedIds,
                AsOf = asOf,
            },
            cancellationToken);

        return Ok(response);
    }

    /// <summary>
    /// Returns the detailed cost breakdown of a single product.
    /// Includes line-by-line BOM detail and warnings.
    /// </summary>
    /// <param name="productId">Product identifier.</param>
    /// <param name="asOf">Optional date in yyyy-MM-dd format; defaults to today.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpGet("{productId:guid}")]
    public async Task<ActionResult<GetProductCostResponse>> GetSingle(
        Guid productId,
        [FromQuery] DateOnly? asOf,
        CancellationToken cancellationToken)
    {
        GetProductCostResponse response = await _mediator.Send(
            new GetProductCostQuery
            {
                ProductId = productId,
                AsOf = asOf,
            },
            cancellationToken);

        if (!response.Found)
        {
            return NotFound();
        }

        return Ok(response);
    }
}
