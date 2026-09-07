using Delobytes.App.Backend.Integrations.Application.DTOs.Connections;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Integrations.Application.Queries.GetConnections;

public class GetConnectionsQueryHandler : IRequestHandler<GetConnectionsQuery, GetConnectionsResponse>
{
    private readonly IConnectionRepository _connectionRepository;

    public GetConnectionsQueryHandler(IConnectionRepository connectionRepository)
    {
        _connectionRepository = connectionRepository;
    }

    public async Task<GetConnectionsResponse> Handle(
        GetConnectionsQuery request,
        CancellationToken cancellationToken)
    {
        List<Connection> connections =
            await _connectionRepository.GetAllByTenantAsync(cancellationToken);

        List<ConnectionDto> items = connections
            .Select(c => new ConnectionDto
            {
                Id = c.Id,
                ChannelCode = c.Channel?.Code ?? string.Empty,
                ChannelDisplayName = c.Channel?.DisplayName ?? string.Empty,
                IsActive = c.IsActive,
                LastSyncAt = c.LastSyncAt,
                CreatedAt = c.CreatedAt,
            })
            .ToList();

        return new GetConnectionsResponse { Items = items };
    }
}
