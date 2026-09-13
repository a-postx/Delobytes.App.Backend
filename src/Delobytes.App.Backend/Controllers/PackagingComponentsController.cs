using Delobytes.App.Backend.Catalog.Application.Commands.PackagingComponents.CreatePackagingComponent;
using Delobytes.App.Backend.Catalog.Application.Commands.PackagingComponents.CreatePackagingComponentPrice;
using Delobytes.App.Backend.Catalog.Application.Commands.PackagingComponents.DeletePackagingComponent;
using Delobytes.App.Backend.Catalog.Application.Commands.PackagingComponents.RestorePackagingComponent;
using Delobytes.App.Backend.Catalog.Application.Commands.PackagingComponents.UpdatePackagingComponent;
using Delobytes.App.Backend.Catalog.Application.Queries.PackagingComponents.GetPackagingComponent;
using Delobytes.App.Backend.Catalog.Application.Queries.PackagingComponents.GetPackagingComponents;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Delobytes.App.Backend.Controllers;

/// <summary>
/// CRUD endpoints for the Packaging Components catalog.
/// Price changes are versioned: POST /{id}/prices appends a new price version and
/// deactivates the previous one, while PUT /{id} touches descriptive fields only.
/// </summary>
[ApiController]
[Route("api/catalogs/packaging-components")]
[Authorize]
public class PackagingComponentsController : ControllerBase
{
    private readonly IMediator _mediator;

    public PackagingComponentsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<GetPackagingComponentsResponse>> GetAll(CancellationToken cancellationToken)
    {
        GetPackagingComponentsResponse response = await _mediator.Send(new GetPackagingComponentsQuery(), cancellationToken);
        return Ok(response);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GetPackagingComponentResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        GetPackagingComponentResponse? response = await _mediator.Send(
            new GetPackagingComponentQuery { Id = id },
            cancellationToken);

        if (response == null)
        {
            return NotFound();
        }

        return Ok(response);
    }

    [HttpPost]
    public async Task<ActionResult<CreatePackagingComponentResponse>> Create(
        [FromBody] CreatePackagingComponentRequest request,
        CancellationToken cancellationToken)
    {
        CreatePackagingComponentResponse response = await _mediator.Send(
            new CreatePackagingComponentCommand
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
        [FromBody] UpdatePackagingComponentRequest request,
        CancellationToken cancellationToken)
    {
        UpdatePackagingComponentResponse response = await _mediator.Send(
            new UpdatePackagingComponentCommand
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
    public async Task<ActionResult<CreatePackagingComponentPriceResponse>> CreatePrice(
        Guid id,
        [FromBody] CreatePackagingComponentPriceRequest request,
        CancellationToken cancellationToken)
    {
        CreatePackagingComponentPriceResponse response = await _mediator.Send(
            new CreatePackagingComponentPriceCommand
            {
                PackagingComponentId = id,
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
        RestorePackagingComponentResponse response = await _mediator.Send(
            new RestorePackagingComponentCommand { Id = id },
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
        DeletePackagingComponentResponse response = await _mediator.Send(
            new DeletePackagingComponentCommand { Id = id },
            cancellationToken);

        if (!response.Found)
        {
            return NotFound();
        }

        return NoContent();
    }
}

/// <summary>Request body for creating a packaging component together with its first price version.</summary>
public class CreatePackagingComponentRequest
{
    public string Name { get; set; } = default!;

    public string? Description { get; set; }

    public Catalog.Domain.Enums.Unit Unit { get; set; }

    public decimal PricePerUnit { get; set; }

    public Guid? SupplierId { get; set; }

    /// <summary>Effective date of the first price version.</summary>
    public DateOnly ValidFrom { get; set; }
}

/// <summary>Request body for updating descriptive fields of a packaging component.</summary>
public class UpdatePackagingComponentRequest
{
    public string Name { get; set; } = default!;

    public string? Description { get; set; }

    public Catalog.Domain.Enums.Unit Unit { get; set; }
}

/// <summary>Request body for appending a new price version.</summary>
public class CreatePackagingComponentPriceRequest
{
    public decimal PricePerUnit { get; set; }

    public Guid? SupplierId { get; set; }

    public DateOnly ValidFrom { get; set; }
}
