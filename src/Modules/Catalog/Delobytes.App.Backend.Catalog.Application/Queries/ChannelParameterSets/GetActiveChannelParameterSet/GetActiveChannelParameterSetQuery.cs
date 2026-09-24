using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Queries.ChannelParameterSets.GetActiveChannelParameterSet;

public class GetActiveChannelParameterSetQuery : IRequest<GetActiveChannelParameterSetResponse>
{
    public Guid ChannelId { get; set; }
}
