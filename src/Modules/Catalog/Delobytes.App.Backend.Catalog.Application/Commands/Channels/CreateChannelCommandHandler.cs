using Delobytes.App.Backend.Contracts.Authorization;
using MediatR;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;

namespace Delobytes.App.Backend.Catalog.Application.Commands.Channels;

/// <summary>
/// Creates a sales channel (Channel) as an independent business entity.
/// A Connection (Integrations module) can be attached to it afterwards, but the channel
/// itself, its costs and its historical data exist regardless of connection status.
/// </summary>
public class CreateChannelCommand : IRequest<CreateChannelResponse>, IRequireRole
{
    public string Name { get; set; } = default!;

    /// <summary>
    /// Optional reference to a system channel template id (Wildberries, Ozon, etc.) from the
    /// Integrations module. Loose reference by design — no cross-module FK. Null means a custom channel.
    /// </summary>
    public Guid? SystemChannelTemplateId { get; set; }

    /// <summary>
    /// System channel template code (e.g. "wildberries", "ozon", "yandex.kit"), or null for a
    /// custom channel. Passed by the caller instead of looked up, because Catalog has no FK
    /// reference to SystemChannelTemplate in Integrations.
    /// </summary>
    public string? Code { get; set; }

    /// <summary>Custom API URL, only meaningful when SystemChannelTemplateId is null.</summary>
    public string? CustomApiUrl { get; set; }

    public Role[] AllowedRoles => new[] { Role.Manager, Role.Administrator };
}

public class CreateChannelResponse
{
    public Guid Id { get; set; }
}

public class CreateChannelCommandHandler : IRequestHandler<CreateChannelCommand, CreateChannelResponse>
{
    private readonly IChannelRepository _repository;

    public CreateChannelCommandHandler(IChannelRepository repository)
    {
        _repository = repository;
    }

    public async Task<CreateChannelResponse> Handle(CreateChannelCommand request, CancellationToken cancellationToken)
    {
        Channel channel = new Channel
        {
            Id = Guid.NewGuid(),
            SystemChannelTemplateId = request.SystemChannelTemplateId,
            Name = request.Name,
            Code = request.Code,
            CustomApiUrl = request.CustomApiUrl,
            IsCustom = request.SystemChannelTemplateId == null,
            IsActive = true
        };

        _repository.Add(channel);
        await _repository.SaveChangesAsync(cancellationToken);

        return new CreateChannelResponse { Id = channel.Id };
    }
}
