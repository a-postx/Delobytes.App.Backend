using Delobytes.App.Backend.Catalog.Application.Commands.BomLines.CreateBomLine;
using Delobytes.App.Backend.Catalog.Application.Commands.BomLines.DeleteBomLine;
using Delobytes.App.Backend.Catalog.Application.Commands.BomLines.UpsertProductBom;
using Delobytes.App.Backend.Catalog.Application.Queries.BomLines.GetProductBom;
using Delobytes.App.Backend.Catalog.Application.Queries.BomLines.GetProductBomHistory;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Delobytes.App.Backend.Controllers;

[ApiController]
[Route("api/catalogs/products/{productId:guid}/bom")]
[Authorize]
public class BomLinesController : ControllerBase
{
    private readonly IMediator _mediator;

    public BomLinesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<GetProductBomResponse>> Get(Guid productId, CancellationToken cancellationToken)
    {
        GetProductBomResponse response = await _mediator.Send(new GetProductBomQuery { ProductId = productId }, cancellationToken);
        return Ok(response);
    }

    [HttpGet("history")]
    public async Task<ActionResult<GetProductBomHistoryResponse>> GetHistory(Guid productId, CancellationToken cancellationToken)
    {
        GetProductBomHistoryResponse response = await _mediator.Send(new GetProductBomHistoryQuery { ProductId = productId }, cancellationToken);
        return Ok(response);
    }

    [HttpPut]
    public async Task<ActionResult<UpsertProductBomResponse>> Upsert(Guid productId, [FromBody] UpsertProductBomRequest request, CancellationToken cancellationToken)
    {
        UpsertProductBomResponse response = await _mediator.Send(new UpsertProductBomCommand { ProductId = productId, Lines = request.Lines }, cancellationToken);
        return Ok(response);
    }

    [HttpPost("lines")]
    public async Task<ActionResult<CreateBomLineResponse>> CreateLine(Guid productId, [FromBody] CreateBomLineRequest request, CancellationToken cancellationToken)
    {
        CreateBomLineResponse response = await _mediator.Send(new CreateBomLineCommand { ProductId = productId, ComponentId = request.ComponentId, Quantity = request.Quantity, ValidFrom = request.ValidFrom }, cancellationToken);
        return Ok(response);
    }

    [HttpDelete("lines/{id:guid}")]
    public async Task<IActionResult> DeleteLine(Guid id, CancellationToken cancellationToken)
    {
        await _mediator.Send(new DeleteBomLineCommand { Id = id }, cancellationToken);
        return NoContent();
    }
}

public class UpsertProductBomRequest
{
    public List<UpsertProductBomItem> Lines { get; set; } = new List<UpsertProductBomItem>();
}

public class CreateBomLineRequest
{
    public Guid ComponentId { get; set; }
    public decimal Quantity { get; set; }
    public DateOnly ValidFrom { get; set; }
}
