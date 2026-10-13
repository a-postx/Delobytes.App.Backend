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

    /// <summary>
    /// Returns components filtered by status and search term, sorted and optionally paged.
    /// Omitted isActive means both statuses. An omitted search keeps the full list; when supplied
    /// it is trimmed, capped at 200 characters and matched case-insensitively as a substring of
    /// the component name. Omitted page keeps the legacy behaviour relied upon by the product BOM
    /// editor: the full list is returned and no Skip/Take is applied. When page is supplied,
    /// pageSize defaults to 50 and is clamped to 1..200, sortBy must be one of name (default),
    /// supplier, validFrom, createdAt, and sortDir is asc (default) or desc. Sorting by supplier or
    /// validFrom uses the component's active price version; a component without an active price
    /// always sorts last, in both directions. Invalid sortBy, sortDir or page values fall back
    /// silently to their defaults instead of failing the request. Set includeCounts to also
    /// receive per-status totals for the filter tabs, computed over the searched result set.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(GetComponentsResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<GetComponentsResponse>> GetAll(
        [FromQuery] bool? isActive,
        [FromQuery] string? search,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDir,
        [FromQuery] bool? includeCounts,
        CancellationToken cancellationToken)
    {
        GetComponentsResponse response = await _mediator.Send(
            new GetComponentsQuery
            {
                IsActive = isActive,
                Search = search,
                Page = page,
                PageSize = pageSize,
                SortBy = sortBy,
                SortDir = sortDir,
                IncludeCounts = includeCounts ?? false,
            },
            cancellationToken);

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
                Category = request.Category,
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
                Category = request.Category,
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

    /// <summary>Категория компонента, определяющая статью себестоимости. По умолчанию — материал.</summary>
    public ComponentCategory Category { get; set; } = ComponentCategory.Material;

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

    /// <summary>Категория компонента, определяющая статью себестоимости. По умолчанию — материал.</summary>
    public ComponentCategory Category { get; set; } = ComponentCategory.Material;
}

/// <summary>Request body for appending a new price version.</summary>
public class CreateComponentPriceRequest
{
    public decimal PricePerUnit { get; set; }

    public Guid? SupplierId { get; set; }

    public DateOnly ValidFrom { get; set; }
}
