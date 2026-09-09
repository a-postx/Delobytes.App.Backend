using Delobytes.App.Backend.Catalog.Application.Commands.TariffGrids.CreateTariffGrid;
using Delobytes.App.Backend.Catalog.Application.Commands.TariffGrids.DeleteTariffGrid;
using Delobytes.App.Backend.Catalog.Application.Commands.TariffGrids.UpdateTariffGrid;
using Delobytes.App.Backend.Catalog.Application.Queries.TariffGrids.GetTariffGrid;
using Delobytes.App.Backend.Catalog.Application.Queries.TariffGrids.GetTariffGrids;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Delobytes.App.Backend.Controllers;

/// <summary>
/// CRUD endpoints for the Tariff Grids catalog (WB and FF).
/// </summary>
[ApiController]
[Route("api/catalogs/tariff-grids")]
[Authorize]
public class TariffGridsController : ControllerBase
{
    private readonly IMediator _mediator;

    /// <summary>
    /// Initializes a new instance of the <see cref="TariffGridsController"/> class.
    /// </summary>
    public TariffGridsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Returns all tariff grids, optionally filtered by type.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<GetTariffGridsResponse>> GetAll(
        [FromQuery] TariffType? tariffType,
        CancellationToken cancellationToken)
    {
        GetTariffGridsResponse response = await _mediator.Send(
            new GetTariffGridsQuery { TariffType = tariffType },
            cancellationToken);

        return Ok(response);
    }

    /// <summary>
    /// Returns a single tariff grid with its entries.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GetTariffGridResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        GetTariffGridResponse? response = await _mediator.Send(
            new GetTariffGridQuery { Id = id },
            cancellationToken);

        if (response == null)
        {
            return NotFound();
        }

        return Ok(response);
    }

    /// <summary>
    /// Creates a new tariff grid with its threshold/rate entries. Requires Manager or Administrator role.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<CreateTariffGridResponse>> Create(
        [FromBody] CreateTariffGridApiRequest request,
        CancellationToken cancellationToken)
    {
        CreateTariffGridResponse response = await _mediator.Send(
            new CreateTariffGridCommand
            {
                Name = request.Name,
                TariffType = request.TariffType,
                ValidFrom = request.ValidFrom,
                ChannelId = request.ChannelId,
                Entries = request.Entries.Select(e => new TariffGridEntryRequest
                {
                    RegionOrCity = e.RegionOrCity,
                    VolumeThresholdLiters = e.VolumeThresholdLiters,
                    Rate = e.Rate,
                }).ToList(),
            },
            cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = response.Id }, response);
    }

    /// <summary>
    /// Updates name and active status of a tariff grid. Entries are immutable after creation. Requires Manager or Administrator role.
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult> Update(
        Guid id,
        [FromBody] UpdateTariffGridApiRequest request,
        CancellationToken cancellationToken)
    {
        UpdateTariffGridResponse response = await _mediator.Send(
            new UpdateTariffGridCommand
            {
                Id = id,
                Name = request.Name,
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
    /// Soft-deletes (deactivates) a tariff grid. Requires Manager or Administrator role.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<ActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        DeleteTariffGridResponse response = await _mediator.Send(
            new DeleteTariffGridCommand { Id = id },
            cancellationToken);

        if (!response.Found)
        {
            return NotFound();
        }

        return NoContent();
    }
}

/// <summary>Request body for creating a tariff grid.</summary>
public class CreateTariffGridApiRequest
{
    public string Name { get; set; } = default!;

    public TariffType TariffType { get; set; }

    public DateOnly ValidFrom { get; set; }

    public Guid? ChannelId { get; set; }

    public IList<TariffGridEntryApiRequest> Entries { get; set; } = new List<TariffGridEntryApiRequest>();
}

/// <summary>A single row in the tariff grid request.</summary>
public class TariffGridEntryApiRequest
{
    public string RegionOrCity { get; set; } = default!;

    public decimal? VolumeThresholdLiters { get; set; }

    public decimal Rate { get; set; }
}

/// <summary>Request body for updating a tariff grid.</summary>
public class UpdateTariffGridApiRequest
{
    public string Name { get; set; } = default!;

    public bool IsActive { get; set; }
}
