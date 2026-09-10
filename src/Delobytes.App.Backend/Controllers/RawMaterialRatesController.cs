using Delobytes.App.Backend.Catalog.Application.Commands.RawMaterialRates.CreateRawMaterialRate;
using Delobytes.App.Backend.Catalog.Application.Commands.RawMaterialRates.DeleteRawMaterialRate;
using Delobytes.App.Backend.Catalog.Application.Queries.RawMaterialRates.GetAllRawMaterialRates;
using Delobytes.App.Backend.Catalog.Application.Queries.RawMaterialRates.GetRawMaterialRates;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Delobytes.App.Backend.Controllers;

/// <summary>
/// Endpoints for the Raw Material Rates catalog.
/// Creating a new rate adds a versioned record; previous records are never modified.
/// Deletion is a soft-delete: IsActive is set to false, the record is retained for historical accuracy.
/// </summary>
[ApiController]
[Route("api/catalogs/raw-material-rates")]
[Authorize]
public class RawMaterialRatesController : ControllerBase
{
    private readonly IMediator _mediator;

    public RawMaterialRatesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>Returns all active raw material rates across all products.</summary>
    [HttpGet]
    public async Task<ActionResult<GetAllRawMaterialRatesResponse>> GetAll(CancellationToken cancellationToken)
    {
        GetAllRawMaterialRatesResponse response = await _mediator.Send(
            new GetAllRawMaterialRatesQuery(),
            cancellationToken);

        return Ok(response);
    }

    /// <summary>Returns the active version history of raw material rates for a given product.</summary>
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

    /// <summary>
    /// Soft-deletes a rate record (IsActive = false).
    /// The record is retained so historical margin calculation snapshots remain valid.
    /// Requires Manager or Administrator role.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        DeleteRawMaterialRateResponse response = await _mediator.Send(
            new DeleteRawMaterialRateCommand { Id = id },
            cancellationToken);

        if (!response.Found)
        {
            return NotFound();
        }

        return NoContent();
    }
}

/// <summary>Request body for adding a raw material rate version.</summary>
public class CreateRawMaterialRateApiRequest
{
    public Guid ProductId { get; set; }

    public decimal CostPerUnit { get; set; }

    public DateOnly ValidFrom { get; set; }
}
