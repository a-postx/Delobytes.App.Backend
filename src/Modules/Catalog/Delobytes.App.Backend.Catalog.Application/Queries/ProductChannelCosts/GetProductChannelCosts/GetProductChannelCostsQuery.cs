using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Queries.ProductChannelCosts.GetProductChannelCosts;

public class GetProductChannelCostsQuery : IRequest<GetProductChannelCostsResponse>
{
    public Guid ProductId { get; set; }

    public Guid? ChannelId { get; set; }
}
