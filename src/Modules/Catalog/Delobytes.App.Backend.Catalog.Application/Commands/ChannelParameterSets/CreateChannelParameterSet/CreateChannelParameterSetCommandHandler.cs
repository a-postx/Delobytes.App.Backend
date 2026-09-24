using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.ChannelParameterSets.CreateChannelParameterSet;

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
            CommissionPercent = request.CommissionPercent,
            AcquiringPercent = request.AcquiringPercent,
            SppPercent = request.SppPercent,
            SppEnabled = request.SppEnabled,
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
