using Delobytes.App.Backend.Catalog.Application.Commands.Components.CreateComponent;
using Delobytes.App.Backend.Catalog.Application.Commands.Components.CreateComponentPrice;
using Delobytes.App.Backend.Catalog.Application.Commands.Components.DeleteComponent;
using Delobytes.App.Backend.Catalog.Application.Commands.Components.RestoreComponent;
using Delobytes.App.Backend.Catalog.Application.Commands.Components.UpdateComponent;
using Delobytes.App.Backend.Catalog.Application.Queries.Components.GetComponent;
using Delobytes.App.Backend.Catalog.Application.Queries.Components.GetComponents;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Delobytes.App.Backend.Controllers;

/// <summary>
/// CRUD endpoints for the Components catalog.
/// Price changes are versioned: POST /{id}/prices appends a new price version and
/// deactivates the previous one, while PUT /{id} touches descriptive fields only.
/// </summary>
[ApiController]
[Route("api/catalogs/components")]
[Authorize]
public class ComponentsController : ControllerBase
{
    private readonly IMediator _mediator;

    public ComponentsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<GetComponentsResponse>> GetAll(CancellationToken cancellationToken)
    {
        GetComponentsResponse response = await _mediator.Send(new GetComponentsQuery(), cancellationToken);
        return Ok(response);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GetComponentResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        GetComponentResponse? response = await _mediator.Send(
            new GetComponentQuery { Id = id },
            cancellationToken);

        if (response == null)
        {
            return NotFound();
        }

        return Ok(response);
    }

    [HttpPost]
    public async Task<ActionResult<CreateComponentResponse>> Create(
        [FromBody] CreateComponentRequest request,
        CancellationToken cancellationToken)
    {
        CreateComponentResponse response = await _mediator.Send(
            new CreateComponentCommand
            {
                Name = request.Name,
                Description = request.Description,
                Unit = request.Unit,
                PricePerUnit = request.PricePerUnit,
                SupplierId = request.SupplierId,
                ValidFrom = request.ValidFrom,
            },
            cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = response.Id }, response);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult> Update(
        Guid id,
        [FromBody] UpdateComponentRequest request,
        CancellationToken cancellationToken)
    {
        UpdateComponentResponse response = await _mediator.Send(
            new UpdateComponentCommand
            {
                Id = id,
                Name = request.Name,
                Description = request.Description,
                Unit = request.Unit,
            },
            cancellationToken);

        if (!response.Found)
        {
            return NotFound();
        }

        return NoContent();
    }

    /// <summary>Appends a new price version for the component.</summary>
    [HttpPost("{id:guid}/prices")]
    public async Task<ActionResult<CreateComponentPriceResponse>> CreatePrice(
        Guid id,
        [FromBody] CreateComponentPriceRequest request,
        CancellationToken cancellationToken)
    {
        CreateComponentPriceResponse response = await _mediator.Send(
            new CreateComponentPriceCommand
            {
                ComponentId = id,
                PricePerUnit = request.PricePerUnit,
                SupplierId = request.SupplierId,
                ValidFrom = request.ValidFrom,
            },
            cancellationToken);

        if (!response.Found)
        {
            return NotFound();
        }

        return Ok(response);
    }

    /// <summary>Restores an archived component and its latest price version.</summary>
    [HttpPost("{id:guid}/restore")]
    public async Task<ActionResult> Restore(Guid id, CancellationToken cancellationToken)
    {
        RestoreComponentResponse response = await _mediator.Send(
            new RestoreComponentCommand { Id = id },
            cancellationToken);

        if (!response.Found)
        {
            return NotFound();
        }

        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        DeleteComponentResponse response = await _mediator.Send(
            new DeleteComponentCommand { Id = id },
            cancellationToken);

        if (!response.Found)
        {
            return NotFound();
        }

        return NoContent();
    }
}

/// <summary>Request body for creating a component together with its first price version.</summary>
public class CreateComponentRequest
{
    public string Name { get; set; } = default!;

    public string? Description { get; set; }

    public Catalog.Domain.Enums.Unit Unit { get; set; }

    public decimal PricePerUnit { get; set; }

    public Guid? SupplierId { get; set; }

    /// <summary>Effective date of the first price version.</summary>
    public DateOnly ValidFrom { get; set; }
}

/// <summary>Request body for updating descriptive fields of a component.</summary>
public class UpdateComponentRequest
{
    public string Name { get; set; } = default!;

    public string? Description { get; set; }

    public Catalog.Domain.Enums.Unit Unit { get; set; }
}

/// <summary>Request body for appending a new price version.</summary>
public class CreateComponentPriceRequest
{
    public decimal PricePerUnit { get; set; }

    public Guid? SupplierId { get; set; }

    public DateOnly ValidFrom { get; set; }
}
