using Delobytes.App.Backend.Integrations.Application.DTOs.Connections;
using MediatR;
using System.Text.Json;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using Delobytes.App.Backend.Integrations.Domain.Entities;

namespace Delobytes.App.Backend.Integrations.Application.Commands;

public class CreateConnectionCommand : IRequest<CreateConnectionResponse>
{
    public Guid ChannelId { get; set; }
    public string SystemChannelTemplateCode { get; set; } = default!;
    public string ApiKey { get; set; } = default!;
    public string? ApiSecret { get; set; }
    public Dictionary<string, string>? Settings { get; set; }
}

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

        bool hasActiveConnection = await _connectionRepository
            .HasActiveConnectionForChannelAsync(request.ChannelId, cancellationToken);

        if (hasActiveConnection)
        {
            throw new ConflictException("К этому каналу продаж уже привязано активное подключение.");
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

        IChannelApiClient apiClient = _channelApiClientFactory.Create(template);

        // No ConnectionId yet: the connection being validated here is not persisted until after
        // this call succeeds. WildberriesAuthHandler is unaffected either way since this call
        // sets its own explicit Authorization header from request.ApiKey.
        AccountInfo? accountInfo = await apiClient.GetAccountInfoAsync(
            null,
            request.ApiKey,
            request.ApiSecret,
            request.Settings,
            cancellationToken);

        Connection connection = new Connection
        {
            Id = Guid.NewGuid(),
            ChannelId = request.ChannelId,
            SystemChannelTemplateId = template.Id,
            Name = template.DisplayName,
            ApiKey = request.ApiKey,
            ApiSecret = request.ApiSecret,
            Settings = request.Settings != null
                ? JsonSerializer.Serialize(request.Settings)
                : null,
            IsActive = true,
            CustomerName = accountInfo?.CustomerName,
            CustomerLegalName = accountInfo?.LegalName,
            CustomerInn = accountInfo?.Inn,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _connectionRepository.Add(connection);
        await _connectionRepository.SaveChangesAsync(cancellationToken);

        // Событие для будущей фоновой синхронизации данных (запуск первичной загрузки заказов и т.п.).
        ConnectionCreatedEvent connectionCreatedEvent = new ConnectionCreatedEvent
        {
            ConnectionId = connection.Id,
            ChannelId = connection.ChannelId,
            SystemChannelTemplateId = template.Id,
        };

        await _eventPublisher.PublishAsync(connectionCreatedEvent, cancellationToken);

        return new CreateConnectionResponse
        {
            ConnectionId = connection.Id,
            ChannelId = connection.ChannelId,
        };
    }
}
