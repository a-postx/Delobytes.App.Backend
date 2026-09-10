using Delobytes.App.Backend.Catalog.Application.Commands.Suppliers.CreateSupplier;
using Delobytes.App.Backend.Catalog.Application.Commands.Suppliers.DeleteSupplier;
using Delobytes.App.Backend.Catalog.Application.Commands.Suppliers.UpdateSupplier;
using Delobytes.App.Backend.Catalog.Application.Queries.Suppliers.GetSupplier;
using Delobytes.App.Backend.Catalog.Application.Queries.Suppliers.GetSuppliers;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Delobytes.App.Backend.Controllers;

/// <summary>
/// CRUD endpoints for the Suppliers catalog.
/// </summary>
[ApiController]
[Route("api/catalogs/suppliers")]
[Authorize]
public class SuppliersController : ControllerBase
{
    private readonly IMediator _mediator;

    public SuppliersController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<GetSuppliersResponse>> GetAll(CancellationToken cancellationToken)
    {
        GetSuppliersResponse response = await _mediator.Send(new GetSuppliersQuery(), cancellationToken);
        return Ok(response);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GetSupplierResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        GetSupplierResponse? response = await _mediator.Send(
            new GetSupplierQuery { Id = id },
            cancellationToken);

        if (response == null)
        {
            return NotFound();
        }

        return Ok(response);
    }

    [HttpPost]
    public async Task<ActionResult<CreateSupplierResponse>> Create(
        [FromBody] CreateSupplierRequest request,
        CancellationToken cancellationToken)
    {
        CreateSupplierResponse response = await _mediator.Send(
            new CreateSupplierCommand
            {
                Name = request.Name,
                ContactInfo = request.ContactInfo,
            },
            cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = response.Id }, response);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult> Update(
        Guid id,
        [FromBody] UpdateSupplierRequest request,
        CancellationToken cancellationToken)
    {
        UpdateSupplierResponse response = await _mediator.Send(
            new UpdateSupplierCommand
            {
                Id = id,
                Name = request.Name,
                ContactInfo = request.ContactInfo,
                IsActive = request.IsActive,
            },
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
        DeleteSupplierResponse response = await _mediator.Send(
            new DeleteSupplierCommand { Id = id },
            cancellationToken);

        if (!response.Found)
        {
            return NotFound();
        }

        return NoContent();
    }
}

/// <summary>Request body for creating a supplier.</summary>
public class CreateSupplierRequest
{
    public string Name { get; set; } = default!;

    public string? ContactInfo { get; set; }
}

/// <summary>Request body for updating a supplier.</summary>
public class UpdateSupplierRequest
{
    public string Name { get; set; } = default!;

    public string? ContactInfo { get; set; }

    public bool IsActive { get; set; }
}
