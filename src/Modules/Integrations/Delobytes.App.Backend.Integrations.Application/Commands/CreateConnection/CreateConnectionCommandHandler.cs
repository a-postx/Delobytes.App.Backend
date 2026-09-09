using System.Text.Json;
using Delobytes.App.Backend.Integrations.Application.DTOs.Connections;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Integrations.Application.Commands.CreateConnection;

public class CreateConnectionCommandHandler : IRequestHandler<CreateConnectionCommand, CreateConnectionResponse>
{
    private readonly ISystemChannelTemplateRepository _templateRepository;
    private readonly IConnectionRepository _connectionRepository;
    private readonly IApiKeyValidatorFactory _validatorFactory;
    private readonly IChannelApiClientFactory _channelApiClientFactory;
    private readonly IEventPublisher _eventPublisher;

    public CreateConnectionCommandHandler(
        ISystemChannelTemplateRepository templateRepository,
        IConnectionRepository connectionRepository,
        IApiKeyValidatorFactory validatorFactory,
        IChannelApiClientFactory channelApiClientFactory,
        IEventPublisher eventPublisher)
    {
        _templateRepository = templateRepository;
        _connectionRepository = connectionRepository;
        _validatorFactory = validatorFactory;
        _channelApiClientFactory = channelApiClientFactory;
        _eventPublisher = eventPublisher;
    }

    public async Task<CreateConnectionResponse> Handle(
        CreateConnectionCommand request,
        CancellationToken cancellationToken)
    {
        SystemChannelTemplate? template = await _templateRepository
            .GetByCodeAsync(request.SystemChannelTemplateCode, cancellationToken);

        if (template == null)
        {
            throw new KeyNotFoundException($"Канал '{request.SystemChannelTemplateCode}' не найден.");
        }

        bool exists = await _connectionRepository
            .ExistsForTemplateAsync(request.SystemChannelTemplateCode, cancellationToken);

        if (exists)
        {
            throw new ConflictException($"Канал '{template.DisplayName}' уже подключён.");
        }

        IApiKeyValidator validator = _validatorFactory.Create(request.SystemChannelTemplateCode);

        ApiKeyValidationResult validationResult = await validator.ValidateAsync(
            request.ApiKey,
            request.ApiSecret,
            request.Settings,
            cancellationToken);

        if (!validationResult.IsValid)
        {
            throw new InvalidOperationException(validationResult.ErrorMessage);
        }

        IChannelApiClient apiClient = _channelApiClientFactory.Create(request.SystemChannelTemplateCode);

        AccountInfo? accountInfo = await apiClient.GetAccountInfoAsync(
            request.ApiKey,
            request.ApiSecret,
            request.Settings,
            cancellationToken);

        Guid channelId = Guid.NewGuid();

        Connection connection = new Connection
        {
            Id = Guid.NewGuid(),
            ChannelId = channelId,
            Name = template.DisplayName,
            ApiKey = request.ApiKey,
            ApiSecret = request.ApiSecret,
            Settings = request.Settings != null
                ? JsonSerializer.Serialize(request.Settings)
                : null,
            IsActive = true,
            CustomerName = accountInfo?.CustomerName,
            LegalName = accountInfo?.LegalName,
            Inn = accountInfo?.Inn,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _connectionRepository.Add(connection);
        await _connectionRepository.SaveChangesAsync(cancellationToken);

        // Catalog subscribes to this event and creates the corresponding Channel.
        ConnectionCreatedEvent channelCreatedEvent = new ConnectionCreatedEvent
        {
            ChannelId = channelId,
            SystemChannelTemplateId = template.Id,
            ChannelName = template.DisplayName,
        };

        await _eventPublisher.PublishAsync(channelCreatedEvent, cancellationToken);

        return new CreateConnectionResponse
        {
            ConnectionId = connection.Id,
            ChannelId = channelId,
        };
    }
}
