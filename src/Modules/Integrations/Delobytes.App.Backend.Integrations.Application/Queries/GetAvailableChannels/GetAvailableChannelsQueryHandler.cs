using Delobytes.App.Backend.Integrations.Application.DTOs.Channels;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Integrations.Application.Queries.GetAvailableChannels;

public class GetAvailableChannelsQueryHandler : IRequestHandler<GetAvailableChannelsQuery, GetAvailableChannelsResponse>
{
    private readonly ISystemChannelTemplateRepository _templateRepository;
    private readonly IConnectionRepository _connectionRepository;

    public GetAvailableChannelsQueryHandler(
        ISystemChannelTemplateRepository templateRepository,
        IConnectionRepository connectionRepository)
    {
        _templateRepository = templateRepository;
        _connectionRepository = connectionRepository;
    }

    public async Task<GetAvailableChannelsResponse> Handle(
        GetAvailableChannelsQuery request,
        CancellationToken cancellationToken)
    {
        List<SystemChannelTemplate> templates =
            await _templateRepository.GetAllActiveAsync(cancellationToken);

        List<Connection> connections =
            await _connectionRepository.GetAllByTenantAsync(cancellationToken);

        // Connection.ChannelId — FK to SystemChannelTemplate.Id
        HashSet<Guid> connectedTemplateIds = connections
            .Select(c => c.ChannelId)
            .ToHashSet();

        List<AvailableChannelDto> items = templates
            .Select(t => new AvailableChannelDto
            {
                Code = t.Code,
                DisplayName = t.DisplayName,
                Description = t.Description,
                ApiVersion = t.ApiVersion,
                IsConnected = connectedTemplateIds.Contains(t.Id),
            })
            .ToList();

        return new GetAvailableChannelsResponse { Items = items };
    }
}
