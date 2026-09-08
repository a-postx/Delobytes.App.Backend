using System.Text.Json;
using System.Transactions;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Integrations.Application.DTOs.Connections;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Integrations.Application.Commands.CreateConnection;

public class CreateConnectionCommandHandler : IRequestHandler<CreateConnectionCommand, CreateConnectionResponse>
{
    private readonly ISystemChannelTemplateRepository _templateRepository;
    private readonly IConnectionRepository _connectionRepository;
    private readonly IChannelRepository _channelRepository;
    private readonly IApiKeyValidatorFactory _validatorFactory;
    private readonly IChannelApiClientFactory _channelApiClientFactory;

    public CreateConnectionCommandHandler(
        ISystemChannelTemplateRepository templateRepository,
        IConnectionRepository connectionRepository,
        IChannelRepository channelRepository,
        IApiKeyValidatorFactory validatorFactory,
        IChannelApiClientFactory channelApiClientFactory)
    {
        _templateRepository = templateRepository;
        _connectionRepository = connectionRepository;
        _channelRepository = channelRepository;
        _validatorFactory = validatorFactory;
        _channelApiClientFactory = channelApiClientFactory;
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

        Channel catalogChannel = new Channel
        {
            Id = Guid.NewGuid(),
            SystemChannelTemplateId = template.Id,
            Name = template.DisplayName,
            IsCustom = false,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        Connection connection = new Connection
        {
            Id = Guid.NewGuid(),
            ChannelId = template.Id,
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

        using (TransactionScope scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
        {
            _channelRepository.Add(catalogChannel);
            await _channelRepository.SaveChangesAsync(cancellationToken);

            _connectionRepository.Add(connection);
            await _connectionRepository.SaveChangesAsync(cancellationToken);

            scope.Complete();
        }

        return new CreateConnectionResponse
        {
            ConnectionId = connection.Id,
            ChannelId = catalogChannel.Id,
        };
    }
}
