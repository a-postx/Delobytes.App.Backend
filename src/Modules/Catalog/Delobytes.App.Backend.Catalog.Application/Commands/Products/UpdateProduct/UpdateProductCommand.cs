using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.Products.UpdateProduct;

public class UpdateProductCommand : IRequest<UpdateProductResponse>
{
    public Guid Id { get; set; }

    public string Name { get; set; } = default!;

    public string? Description { get; set; }
}
