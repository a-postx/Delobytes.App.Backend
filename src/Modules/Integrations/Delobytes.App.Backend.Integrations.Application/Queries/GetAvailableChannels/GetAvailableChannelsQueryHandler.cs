using Delobytes.App.Backend.Integrations.Application.DTOs.Channels;
using Delobytes.App.Backend.Integrations.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Integrations.Application.Queries.GetAvailableChannels;

/// <summary>
/// Returns the catalog of system channel templates available for connecting.
/// Deliberately carries no per-tenant connection/account state — that now lives on
/// Connection (see GetConnectionsQuery) and is joined client-side via Channel.
/// </summary>
public class GetAvailableChannelsQueryHandler : IRequestHandler<GetAvailableChannelsQuery, GetAvailableChannelsResponse>
{
    private readonly ISystemChannelTemplateRepository _templateRepository;

    public GetAvailableChannelsQueryHandler(ISystemChannelTemplateRepository templateRepository)
    {
        _templateRepository = templateRepository;
    }

    public async Task<GetAvailableChannelsResponse> Handle(
        GetAvailableChannelsQuery request,
        CancellationToken cancellationToken)
    {
        List<SystemChannelTemplate> templates =
            await _templateRepository.GetAllActiveAsync(cancellationToken);

        List<AvailableChannelDto> items = templates
            .Select(t => new AvailableChannelDto
            {
                Id = t.Id,
                Code = t.Code,
                DisplayName = t.DisplayName,
                Description = t.Description,
                ApiVersion = t.ApiVersion,
            })
            .ToList();

        return new GetAvailableChannelsResponse { Items = items };
    }
}
