using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.ChannelParameterSets.CreateChannelParameterSet;

public class CreateChannelParameterSetCommand : IRequest<CreateChannelParameterSetResponse>
{
    public Guid ChannelId { get; set; }
    public DateOnly ValidFrom { get; set; }
}
