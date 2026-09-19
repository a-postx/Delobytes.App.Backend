using Delobytes.App.Backend.Catalog.Application.Commands.CostTypes.CreateCostType;
using Delobytes.App.Backend.Catalog.Application.Commands.CostTypes.UpdateCostType;
using Delobytes.App.Backend.Catalog.Application.Queries.CostTypes.GetCostTypes;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Delobytes.App.Backend.Controllers;

/// <summary>
/// CRUD endpoints for the Cost Types catalog.
/// </summary>
[ApiController]
[Route("api/catalogs/cost-types")]
[Authorize]
public class CostTypesController : ControllerBase
{
    private readonly IMediator _mediator;

    public CostTypesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Returns all cost types.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<GetCostTypesResponse>> GetAll(CancellationToken cancellationToken)
    {
        GetCostTypesResponse response = await _mediator.Send(new GetCostTypesQuery(), cancellationToken);
        return Ok(response);
    }

    /// <summary>
    /// Creates a new cost type. Requires Manager or Administrator role.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<CreateCostTypeResponse>> Create(
        [FromBody] CreateCostTypeApiRequest request,
        CancellationToken cancellationToken)
    {
        CreateCostTypeResponse response = await _mediator.Send(
            new CreateCostTypeCommand
            {
                Name = request.Name,
                Description = request.Description,
            },
            cancellationToken);

        return CreatedAtAction(nameof(GetAll), new { id = response.Id }, response);
    }

    /// <summary>
    /// Updates a cost type. Requires Manager or Administrator role.
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult> Update(
        Guid id,
        [FromBody] UpdateCostTypeApiRequest request,
        CancellationToken cancellationToken)
    {
        UpdateCostTypeResponse response = await _mediator.Send(
            new UpdateCostTypeCommand
            {
                Id = id,
                Name = request.Name,
                Description = request.Description,
                IsActive = request.IsActive,
            },
            cancellationToken);

        if (!response.Found)
        {
            return NotFound();
        }

        return NoContent();
    }
}

/// <summary>Request body for creating a cost type.</summary>
public class CreateCostTypeApiRequest
{
    public string Name { get; set; } = default!;

    public string? Description { get; set; }
}

/// <summary>Request body for updating a cost type.</summary>
public class UpdateCostTypeApiRequest
{
    public string Name { get; set; } = default!;

    public string? Description { get; set; }

    public bool IsActive { get; set; }
}
