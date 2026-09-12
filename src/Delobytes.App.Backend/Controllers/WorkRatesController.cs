using Delobytes.App.Backend.Catalog.Application.Commands.WorkRates.CreateWorkRate;
using Delobytes.App.Backend.Catalog.Application.Commands.WorkRates.DeleteWorkRate;
using Delobytes.App.Backend.Catalog.Application.Commands.WorkRates.UpdateWorkRate;
using Delobytes.App.Backend.Catalog.Application.Queries.WorkRates.GetWorkRate;
using Delobytes.App.Backend.Catalog.Application.Queries.WorkRates.GetWorkRates;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Delobytes.App.Backend.Controllers;

/// <summary>
/// CRUD endpoints for the Work Rates catalog.
/// </summary>
[ApiController]
[Route("api/catalogs/work-rates")]
[Authorize]
public class WorkRatesController : ControllerBase
{
    private readonly IMediator _mediator;

    public WorkRatesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<GetWorkRatesResponse>> GetAll(CancellationToken cancellationToken)
    {
        GetWorkRatesResponse response = await _mediator.Send(new GetWorkRatesQuery(), cancellationToken);
        return Ok(response);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GetWorkRateResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        GetWorkRateResponse? response = await _mediator.Send(
            new GetWorkRateQuery { Id = id },
            cancellationToken);

        if (response == null)
        {
            return NotFound();
        }

        return Ok(response);
    }

    [HttpPost]
    public async Task<ActionResult<CreateWorkRateResponse>> Create(
        [FromBody] CreateWorkRateApiRequest request,
        CancellationToken cancellationToken)
    {
        CreateWorkRateResponse response = await _mediator.Send(
            new CreateWorkRateCommand
            {
                Name = request.Name,
                DailyWage = request.DailyWage,
                ValidFrom = request.ValidFrom,
            },
            cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = response.Id }, response);
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        DeleteWorkRateResponse response = await _mediator.Send(
            new DeleteWorkRateCommand { Id = id },
            cancellationToken);

        if (!response.Found)
        {
            return NotFound();
        }

        return NoContent();
    }
}

public class CreateWorkRateApiRequest
{
    public string Name { get; set; } = default!;

    public decimal DailyWage { get; set; }

    public DateOnly ValidFrom { get; set; }
}

public class UpdateWorkRateApiRequest
{
    public string Name { get; set; } = default!;

    public decimal DailyWage { get; set; }

    public DateOnly ValidFrom { get; set; }

    public bool IsActive { get; set; }
}
