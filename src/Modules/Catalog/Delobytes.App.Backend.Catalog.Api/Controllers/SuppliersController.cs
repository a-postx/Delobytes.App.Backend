using Delobytes.App.Backend.Catalog.Application.Commands.Suppliers;
using Delobytes.App.Backend.Catalog.Application.Queries.Suppliers;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Delobytes.App.Backend.Catalog.Api.Controllers;

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

    /// <summary>Gets all suppliers.</summary>
    [HttpGet]
    public async Task<ActionResult<GetSuppliersResponse>> GetAll(CancellationToken cancellationToken)
    {
        GetSuppliersResponse response = await _mediator.Send(new GetSuppliersQuery(), cancellationToken);
        return Ok(response);
    }

    /// <summary>Gets the supplier by ID.</summary>
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

    /// <summary>Creates the supplier.</summary>
    [HttpPost]
    public async Task<ActionResult<CreateSupplierResponse>> Create(
        [FromBody] CreateSupplierRequest request,
        CancellationToken cancellationToken)
    {
        CreateSupplierResponse response = await _mediator.Send(
            new CreateSupplierCommand
            {
                Inn = request.Inn,
                Name = request.Name,
                Description = request.Description,
                Phone = request.Phone,
                Email = request.Email,
            },
            cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = response.Id }, response);
    }

    /// <summary>Updates the supplier.</summary>
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
                Inn = request.Inn,
                Name = request.Name,
                Description = request.Description,
                Phone = request.Phone,
                Email = request.Email,
                IsActive = request.IsActive,
            },
            cancellationToken);

        if (!response.Found)
        {
            return NotFound();
        }

        return NoContent();
    }

    /// <summary>Deletes the supplier.</summary>
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
    public string Inn { get; set; } = default!;

    public string Name { get; set; } = default!;

    public string? Description { get; set; }

    public string? Phone { get; set; }

    public string? Email { get; set; }
}

/// <summary>Request body for updating a supplier.</summary>
public class UpdateSupplierRequest
{
    public string Inn { get; set; } = default!;

    public string Name { get; set; } = default!;

    public string? Description { get; set; }

    public string? Phone { get; set; }

    public string? Email { get; set; }

    public bool IsActive { get; set; }
}
