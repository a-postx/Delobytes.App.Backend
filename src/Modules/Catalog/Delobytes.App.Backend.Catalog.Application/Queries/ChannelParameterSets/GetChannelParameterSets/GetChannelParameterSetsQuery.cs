using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Queries.ChannelParameterSets.GetChannelParameterSets;

public class GetChannelParameterSetsQuery : IRequest<GetChannelParameterSetsResponse>
{
    public Guid ChannelId { get; set; }
}
