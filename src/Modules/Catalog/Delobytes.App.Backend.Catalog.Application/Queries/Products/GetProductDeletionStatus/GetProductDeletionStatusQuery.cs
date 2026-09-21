using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Queries.Products.GetProductDeletionStatus;

public class GetProductDeletionStatusQuery : IRequest<GetProductDeletionStatusResponse>
{
    public Guid ProductId { get; set; }
}
