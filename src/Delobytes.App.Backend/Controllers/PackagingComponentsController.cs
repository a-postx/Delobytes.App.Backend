using Delobytes.App.Backend.Catalog.Application.Commands.PackagingComponents.CreatePackagingComponent;
using Delobytes.App.Backend.Catalog.Application.Commands.PackagingComponents.DeletePackagingComponent;
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
/// </summary>
[ApiController]
[Route("api/catalogs/packaging-components")]
[Authorize]
public class PackagingComponentsController : ControllerBase
{
    private readonly IMediator _mediator;

    /// <summary>
    /// Initializes a new instance of the <see cref="PackagingComponentsController"/> class.
    /// </summary>
    public PackagingComponentsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Returns all packaging components for the current tenant.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<GetPackagingComponentsResponse>> GetAll(CancellationToken cancellationToken)
    {
        GetPackagingComponentsResponse response = await _mediator.Send(new GetPackagingComponentsQuery(), cancellationToken);
        return Ok(response);
    }

    /// <summary>
    /// Returns a single packaging component by ID.
    /// </summary>
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

    /// <summary>
    /// Creates a new packaging component. Requires Manager or Administrator role.
    /// </summary>
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
                Supplier = request.Supplier,
            },
            cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = response.Id }, response);
    }

    /// <summary>
    /// Updates an existing packaging component. Requires Manager or Administrator role.
    /// </summary>
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
                PricePerUnit = request.PricePerUnit,
                Supplier = request.Supplier,
                IsActive = request.IsActive,
            },
            cancellationToken);

        if (!response.Found)
        {
            return NotFound();
        }

        return NoContent();
    }

    /// <summary>
    /// Soft-deletes a packaging component. Requires Manager or Administrator role.
    /// </summary>
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

/// <summary>Request body for creating a packaging component.</summary>
public class CreatePackagingComponentRequest
{
    public string Name { get; set; } = default!;

    public string? Description { get; set; }

    public Catalog.Domain.Enums.Unit Unit { get; set; }

    public decimal PricePerUnit { get; set; }

    public string? Supplier { get; set; }
}

/// <summary>Request body for updating a packaging component.</summary>
public class UpdatePackagingComponentRequest
{
    public string Name { get; set; } = default!;

    public string? Description { get; set; }

    public Catalog.Domain.Enums.Unit Unit { get; set; }

    public decimal PricePerUnit { get; set; }

    public string? Supplier { get; set; }

    public bool IsActive { get; set; }
}
