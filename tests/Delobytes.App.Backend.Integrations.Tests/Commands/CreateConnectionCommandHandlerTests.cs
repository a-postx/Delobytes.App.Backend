using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Integrations.Application;
using Delobytes.App.Backend.Integrations.Application.Commands.CreateConnection;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using FluentAssertions;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.Commands;

public class CreateConnectionCommandHandlerTests
{
    private readonly Mock<ISystemChannelTemplateRepository> _templateRepo = new();
    private readonly Mock<IConnectionRepository> _connectionRepo = new();
    private readonly Mock<IChannelRepository> _channelRepo = new();
    private readonly Mock<IApiKeyValidatorFactory> _validatorFactory = new();
    private readonly Mock<IChannelApiClientFactory> _apiClientFactory = new();
    private readonly Mock<IChannelApiClient> _apiClient = new();
    private readonly Mock<IApiKeyValidator> _validator = new();

    private CreateConnectionCommandHandler CreateHandler()
    {
        return new CreateConnectionCommandHandler(
            _templateRepo.Object,
            _connectionRepo.Object,
            _channelRepo.Object,
            _validatorFactory.Object,
            _apiClientFactory.Object);
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
    public async Task Handle_ValidRequest_SavesBothEntitiesAndReturnsResponse()
    {
        SystemChannelTemplate template = BuildTemplate();
        Channel? savedChannel = null;
        Connection? savedConnection = null;

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

        _channelRepo
            .Setup(r => r.Add(It.IsAny<Channel>()))
            .Callback<Channel>(c => savedChannel = c);

        _channelRepo
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _connectionRepo
            .Setup(r => r.Add(It.IsAny<Connection>()))
            .Callback<Connection>(c => savedConnection = c);

        _connectionRepo
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        CreateConnectionCommandHandler handler = CreateHandler();
        CreateConnectionCommand command = BuildCommand();

        Integrations.Application.DTOs.Connections.CreateConnectionResponse result =
            await handler.Handle(command, CancellationToken.None);

        result.Should().NotBeNull();
        result.ConnectionId.Should().NotBeEmpty();
        result.ChannelId.Should().NotBeEmpty();

        savedChannel.Should().NotBeNull();
        savedChannel!.SystemChannelTemplateId.Should().Be(template.Id);
        savedChannel.IsActive.Should().BeTrue();
        savedChannel.IsCustom.Should().BeFalse();

        savedConnection.Should().NotBeNull();
        savedConnection!.IsActive.Should().BeTrue();
        savedConnection.ApiKey.Should().Be(command.ApiKey);
        savedConnection.CustomerName.Should().BeNull();
        savedConnection.LegalName.Should().BeNull();
        savedConnection.Inn.Should().BeNull();
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

        _channelRepo.Setup(r => r.Add(It.IsAny<Channel>()));
        _channelRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        _connectionRepo
            .Setup(r => r.Add(It.IsAny<Connection>()))
            .Callback<Connection>(c => savedConnection = c);

        _connectionRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        CreateConnectionCommand command = new CreateConnectionCommand
        {
            SystemChannelTemplateCode = "ozon",
            ApiKey = "valid-api-key-1234567890",
            Settings = new Dictionary<string, string> { ["sellerId"] = "12345" },
        };

        CreateConnectionCommandHandler handler = CreateHandler();
        await handler.Handle(command, CancellationToken.None);

        savedConnection!.Settings.Should().NotBeNullOrEmpty();
        savedConnection.Settings.Should().Contain("sellerId");
        savedConnection.Settings.Should().Contain("12345");
    }
}
