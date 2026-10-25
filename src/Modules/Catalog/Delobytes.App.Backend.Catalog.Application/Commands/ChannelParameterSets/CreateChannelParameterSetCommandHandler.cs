using MediatR;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;

namespace Delobytes.App.Backend.Catalog.Application.Commands.ChannelParameterSets;

public class CreateChannelParameterSetCommand : IRequest<CreateChannelParameterSetResponse>
{
    public Guid ChannelId { get; set; }
    public DateOnly ValidFrom { get; set; }
}

public class CreateChannelParameterSetResponse
{
    public Guid Id { get; set; }
    public bool ChannelFound { get; set; }
}

public class CreateChannelParameterSetCommandHandler : IRequestHandler<CreateChannelParameterSetCommand, CreateChannelParameterSetResponse>
{
    private readonly IChannelParameterSetRepository _parameterSetRepository;
    private readonly IChannelRepository _channelRepository;

    public CreateChannelParameterSetCommandHandler(
        IChannelParameterSetRepository parameterSetRepository,
        IChannelRepository channelRepository)
    {
        _parameterSetRepository = parameterSetRepository;
        _channelRepository = channelRepository;
    }

    public async Task<CreateChannelParameterSetResponse> Handle(
        CreateChannelParameterSetCommand request,
        CancellationToken cancellationToken)
    {
        Channel? channel = await _channelRepository.GetByIdAsync(request.ChannelId, cancellationToken);

        if (channel == null)
        {
            return new CreateChannelParameterSetResponse { ChannelFound = false };
        }

        ChannelParameterSet parameterSet = new ChannelParameterSet
        {
            Id = Guid.NewGuid(),
            ChannelId = request.ChannelId,
            ValidFrom = request.ValidFrom,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _parameterSetRepository.Add(parameterSet);
        await _parameterSetRepository.SaveChangesAsync(cancellationToken);

        return new CreateChannelParameterSetResponse
        {
            Id = parameterSet.Id,
            ChannelFound = true,
        };
    }
}
