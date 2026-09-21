using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.Products.ArchiveProduct;

public class ArchiveProductCommand : IRequest<ArchiveProductResponse>
{
    public Guid ProductId { get; set; }
}
