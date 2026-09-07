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

        Dictionary<Guid, Connection> connectionByTemplateId = connections
            .ToDictionary(c => c.ChannelId, c => c);

        List<AvailableChannelDto> items = templates
            .Select(t =>
            {
                bool isConnected = connectionByTemplateId.TryGetValue(t.Id, out Connection? conn);
                return new AvailableChannelDto
                {
                    Code = t.Code,
                    DisplayName = t.DisplayName,
                    Description = t.Description,
                    ApiVersion = t.ApiVersion,
                    IsConnected = isConnected,
                    ConnectionId = isConnected ? conn!.Id : null,
                    // Show last 6 chars, mask the rest
                    MaskedApiKey = isConnected && conn!.ApiKey.Length >= 6
                        ? new string('*', conn.ApiKey.Length - 6) + conn.ApiKey[^6..]
                        : null,
                };
            })
            .ToList();

        return new GetAvailableChannelsResponse { Items = items };
    }
}
