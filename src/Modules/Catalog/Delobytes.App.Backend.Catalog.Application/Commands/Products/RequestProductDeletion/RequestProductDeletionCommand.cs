using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.Products.RequestProductDeletion;

public class RequestProductDeletionCommand : IRequest<RequestProductDeletionResponse>
{
    public Guid ProductId { get; set; }
}
