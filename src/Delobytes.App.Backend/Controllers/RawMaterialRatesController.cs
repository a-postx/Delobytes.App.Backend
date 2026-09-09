using Delobytes.App.Backend.Catalog.Application.Commands.RawMaterialRates.CreateRawMaterialRate;
using Delobytes.App.Backend.Catalog.Application.Queries.RawMaterialRates.GetRawMaterialRates;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Delobytes.App.Backend.Controllers;

/// <summary>
/// Endpoints for the Raw Material Rates catalog.
/// Creating a new rate adds a versioned record; previous records are never modified.
/// </summary>
[ApiController]
[Route("api/catalogs/raw-material-rates")]
[Authorize]
public class RawMaterialRatesController : ControllerBase
{
    private readonly IMediator _mediator;

    /// <summary>
    /// Initializes a new instance of the <see cref="RawMaterialRatesController"/> class.
    /// </summary>
    public RawMaterialRatesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Returns the full version history of raw material rates for a given product.
    /// </summary>
    [HttpGet("by-product/{productId:guid}")]
    public async Task<ActionResult<GetRawMaterialRatesResponse>> GetByProduct(
        Guid productId,
        CancellationToken cancellationToken)
    {
        GetRawMaterialRatesResponse response = await _mediator.Send(
            new GetRawMaterialRatesQuery { ProductId = productId },
            cancellationToken);

        return Ok(response);
    }

    /// <summary>
    /// Adds a new raw material rate version for a product.
    /// Previous versions are not affected. Requires Manager or Administrator role.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<CreateRawMaterialRateResponse>> Create(
        [FromBody] CreateRawMaterialRateApiRequest request,
        CancellationToken cancellationToken)
    {
        CreateRawMaterialRateResponse response = await _mediator.Send(
            new CreateRawMaterialRateCommand
            {
                ProductId = request.ProductId,
                CostPerUnit = request.CostPerUnit,
                ValidFrom = request.ValidFrom,
            },
            cancellationToken);

        return CreatedAtAction(
            nameof(GetByProduct),
            new { productId = request.ProductId },
            response);
    }
}

/// <summary>Request body for adding a raw material rate version.</summary>
public class CreateRawMaterialRateApiRequest
{
    public Guid ProductId { get; set; }

    public decimal CostPerUnit { get; set; }

    public DateOnly ValidFrom { get; set; }
}
