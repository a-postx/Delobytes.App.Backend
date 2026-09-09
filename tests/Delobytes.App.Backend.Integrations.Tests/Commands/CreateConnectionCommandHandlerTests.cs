using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Integrations.Application;
using Delobytes.App.Backend.Integrations.Application.Commands.CreateConnection;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using FluentAssertions;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.Commands;

public class CreateConnectionCommandHandlerTests
{
    private readonly Mock<ISystemChannelTemplateRepository> _templateRepo = new();
    private readonly Mock<IConnectionRepository> _connectionRepo = new();
    private readonly Mock<IApiKeyValidatorFactory> _validatorFactory = new();
    private readonly Mock<IChannelApiClientFactory> _apiClientFactory = new();
    private readonly Mock<IChannelApiClient> _apiClient = new();
    private readonly Mock<IApiKeyValidator> _validator = new();
    private readonly Mock<IEventPublisher> _eventPublisher = new();

    private CreateConnectionCommandHandler CreateHandler()
    {
        return new CreateConnectionCommandHandler(
            _templateRepo.Object,
            _connectionRepo.Object,
            _validatorFactory.Object,
            _apiClientFactory.Object,
            _eventPublisher.Object);
    }

    private static SystemChannelTemplate BuildTemplate(string code = "wildberries")
    {
        return new SystemChannelTemplate
        {
            Id = Guid.NewGuid(),
            Code = code,
            DisplayName = "Wildberries",
            ApiBaseUrl = "https://suppliers-api.wildberries.ru",
            ApiVersion = "v3",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    private static CreateConnectionCommand BuildCommand(string code = "wildberries")
    {
        return new CreateConnectionCommand
        {
            SystemChannelTemplateCode = code,
            ApiKey = "valid-api-key-1234567890",
            ApiSecret = null,
            Settings = null,
        };
    }

    [Fact]
    public async Task Handle_TemplateNotFound_ThrowsKeyNotFoundException()
    {
        _templateRepo
            .Setup(r => r.GetByCodeAsync("unknown", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SystemChannelTemplate?)null);

        CreateConnectionCommandHandler handler = CreateHandler();
        CreateConnectionCommand command = BuildCommand("unknown");

        Func<Task> act = () => handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("*unknown*");
    }

    [Fact]
    public async Task Handle_ConnectionAlreadyExists_ThrowsConflictException()
    {
        SystemChannelTemplate template = BuildTemplate();

        _templateRepo
            .Setup(r => r.GetByCodeAsync("wildberries", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        _connectionRepo
            .Setup(r => r.ExistsForTemplateAsync("wildberries", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        CreateConnectionCommandHandler handler = CreateHandler();

        Func<Task> act = () => handler.Handle(BuildCommand(), CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("*Wildberries*");
    }

    [Fact]
    public async Task Handle_ApiKeyValidationFails_ThrowsInvalidOperationException()
    {
        SystemChannelTemplate template = BuildTemplate();

        _templateRepo
            .Setup(r => r.GetByCodeAsync("wildberries", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        _connectionRepo
            .Setup(r => r.ExistsForTemplateAsync("wildberries", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _validatorFactory
            .Setup(f => f.Create("wildberries"))
            .Returns(_validator.Object);

        _validator
            .Setup(v => v.ValidateAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<Dictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiKeyValidationResult.Failure("Неверный API-ключ Wildberries."));

        CreateConnectionCommandHandler handler = CreateHandler();

        Func<Task> act = () => handler.Handle(BuildCommand(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Неверный API-ключ*");
    }

    [Fact]
    public async Task Handle_ValidRequest_SavesConnectionAndPublishesCreatedEvent()
    {
        SystemChannelTemplate template = BuildTemplate();
        Connection? savedConnection = null;
        ConnectionCreatedEvent? publishedEvent = null;

        _templateRepo
            .Setup(r => r.GetByCodeAsync("wildberries", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        _connectionRepo
            .Setup(r => r.ExistsForTemplateAsync("wildberries", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _validatorFactory
            .Setup(f => f.Create("wildberries"))
            .Returns(_validator.Object);

        _validator
            .Setup(v => v.ValidateAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<Dictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiKeyValidationResult.Success());

        _apiClientFactory
            .Setup(f => f.Create("wildberries"))
            .Returns(_apiClient.Object);

        _apiClient
            .Setup(c => c.GetAccountInfoAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<Dictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((AccountInfo?)null);

        _connectionRepo
            .Setup(r => r.Add(It.IsAny<Connection>()))
            .Callback<Connection>(c => savedConnection = c);

        _connectionRepo
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _eventPublisher
            .Setup(p => p.PublishAsync(It.IsAny<ConnectionCreatedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<ConnectionCreatedEvent, CancellationToken>((e, _) => publishedEvent = e)
            .Returns(Task.CompletedTask);

        CreateConnectionCommandHandler handler = CreateHandler();
        CreateConnectionCommand command = BuildCommand();

        Integrations.Application.DTOs.Connections.CreateConnectionResponse result =
            await handler.Handle(command, CancellationToken.None);

        result.Should().NotBeNull();
        result.ConnectionId.Should().NotBeEmpty();
        result.ChannelId.Should().NotBeEmpty();

        savedConnection.Should().NotBeNull();
        savedConnection!.IsActive.Should().BeTrue();
        savedConnection.ApiKey.Should().Be(command.ApiKey);

        publishedEvent.Should().NotBeNull();
        publishedEvent!.ChannelId.Should().Be(result.ChannelId);
        publishedEvent.ChannelName.Should().Be(template.DisplayName);
        publishedEvent.SystemChannelTemplateId.Should().Be(template.Id);
    }

    [Fact]
    public async Task Handle_WithSettings_SerializesSettingsToJson()
    {
        SystemChannelTemplate template = BuildTemplate("ozon");
        Connection? savedConnection = null;

        _templateRepo
            .Setup(r => r.GetByCodeAsync("ozon", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        _connectionRepo
            .Setup(r => r.ExistsForTemplateAsync("ozon", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _apiClientFactory
            .Setup(f => f.Create("ozon"))
            .Returns(_apiClient.Object);

        _apiClient
            .Setup(c => c.GetAccountInfoAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<Dictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((AccountInfo?)null);

        _validatorFactory
            .Setup(f => f.Create("ozon"))
            .Returns(_validator.Object);

        _validator
            .Setup(v => v.ValidateAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<Dictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiKeyValidationResult.Success());

        _connectionRepo
            .Setup(r => r.Add(It.IsAny<Connection>()))
            .Callback<Connection>(c => savedConnection = c);

        _connectionRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        _eventPublisher
            .Setup(p => p.PublishAsync(It.IsAny<ConnectionCreatedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        Dictionary<string, string> settings = new Dictionary<string, string>
        {
            ["sellerId"] = "99999",
        };

        CreateConnectionCommandHandler handler = CreateHandler();
        await handler.Handle(new CreateConnectionCommand
        {
            SystemChannelTemplateCode = "ozon",
            ApiKey = "ozon-key",
            Settings = settings,
        }, CancellationToken.None);

        savedConnection.Should().NotBeNull();
        savedConnection!.Settings.Should().NotBeNullOrEmpty();
        savedConnection.Settings.Should().Contain("sellerId");
        savedConnection.Settings.Should().Contain("99999");
    }
}
