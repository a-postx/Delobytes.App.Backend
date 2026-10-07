using Delobytes.App.Backend.Catalog.Application.Commands.ProductWorkRates.CreateProductWorkRate;
using Delobytes.App.Backend.Catalog.Application.Commands.ProductWorkRates.DeleteProductWorkRate;
using Delobytes.App.Backend.Catalog.Application.Commands.ProductWorkRates.UpdateProductWorkRate;
using Delobytes.App.Backend.Catalog.Application.Queries.ProductWorkRates.GetAllProductWorkRates;
using Delobytes.App.Backend.Catalog.Application.Queries.ProductWorkRates.GetProductWorkRates;
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
    /// Returns all product work rate versions across all products, active and inactive alike.
    /// GetAllAsync does not filter by status; the active/all/inactive status filter is applied
    /// client-side, as in ComponentsView.vue.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<GetAllProductWorkRatesResponse>> GetAll(CancellationToken cancellationToken)
    {
        GetAllProductWorkRatesResponse response = await _mediator.Send(
            new GetAllProductWorkRatesQuery(),
            cancellationToken);

        return Ok(response);
    }

    /// <summary>Returns the version history of work rates for a given product.</summary>
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
