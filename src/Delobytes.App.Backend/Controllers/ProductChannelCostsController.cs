using Delobytes.App.Backend.Catalog.Application.Commands.ProductChannelCosts.DeleteProductChannelCost;
using Delobytes.App.Backend.Catalog.Application.Commands.ProductChannelCosts.UpsertProductChannelCost;
using Delobytes.App.Backend.Catalog.Application.Queries.ProductChannelCosts.GetProductChannelCosts;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Delobytes.App.Backend.Controllers;

/// <summary>
/// Endpoints for managing per-product per-channel cost entries.
/// </summary>
[ApiController]
[Route("api/catalogs/product-channel-costs")]
[Authorize]
public class ProductChannelCostsController : ControllerBase
{
    private readonly IMediator _mediator;

    public ProductChannelCostsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Returns cost entries for a product, optionally filtered by channel.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<GetProductChannelCostsResponse>> GetByProduct(
        [FromQuery] Guid productId,
        [FromQuery] Guid? channelId,
        CancellationToken cancellationToken)
    {
        GetProductChannelCostsResponse response = await _mediator.Send(
            new GetProductChannelCostsQuery
            {
                ProductId = productId,
                ChannelId = channelId,
            },
            cancellationToken);

        return Ok(response);
    }

    /// <summary>
    /// Creates or updates a cost entry for a product/channel/cost-type combination.
    /// If a record for the same (productId, channelId, costTypeId) already exists, its amount is updated.
    /// Requires Manager or Administrator role.
    /// </summary>
    [HttpPut]
    public async Task<ActionResult<UpsertProductChannelCostResponse>> Upsert(
        [FromBody] UpsertProductChannelCostApiRequest request,
        CancellationToken cancellationToken)
    {
        UpsertProductChannelCostResponse response = await _mediator.Send(
            new UpsertProductChannelCostCommand
            {
                ProductId = request.ProductId,
                ChannelId = request.ChannelId,
                CostTypeId = request.CostTypeId,
                Amount = request.Amount,
            },
            cancellationToken);

        return Ok(response);
    }

    /// <summary>
    /// Deletes a cost entry by ID. Requires Manager or Administrator role.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<ActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        DeleteProductChannelCostResponse response = await _mediator.Send(
            new DeleteProductChannelCostCommand { Id = id },
            cancellationToken);

        if (!response.Found)
        {
            return NotFound();
        }

        return NoContent();
    }
}

/// <summary>Request body for upserting a product channel cost.</summary>
public class UpsertProductChannelCostApiRequest
{
    public Guid ProductId { get; set; }

    public Guid ChannelId { get; set; }

    public Guid CostTypeId { get; set; }

    public decimal Amount { get; set; }
}
