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

public class CreateConnectionAccountInfoTests
{
    private readonly Mock<ISystemChannelTemplateRepository> _templateRepo = new();
    private readonly Mock<IConnectionRepository> _connectionRepo = new();
    private readonly Mock<IChannelRepository> _channelRepo = new();
    private readonly Mock<IApiKeyValidatorFactory> _validatorFactory = new();
    private readonly Mock<IApiKeyValidator> _validator = new();
    private readonly Mock<IChannelApiClientFactory> _apiClientFactory = new();
    private readonly Mock<IChannelApiClient> _apiClient = new();

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
            DisplayName = code == "wildberries" ? "Wildberries"
                : code == "ozon" ? "Ozon"
                : "Яндекс.Кит",
            ApiBaseUrl = "https://example.com",
            ApiVersion = "v1",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    private void SetupHappyPath(SystemChannelTemplate template)
    {
        _templateRepo
            .Setup(r => r.GetByCodeAsync(template.Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        _connectionRepo
            .Setup(r => r.ExistsForTemplateAsync(template.Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _validatorFactory
            .Setup(f => f.Create(template.Code))
            .Returns(_validator.Object);

        _validator
            .Setup(v => v.ValidateAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<Dictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiKeyValidationResult.Success());

        _apiClientFactory
            .Setup(f => f.Create(template.Code))
            .Returns(_apiClient.Object);

        _channelRepo.Setup(r => r.Add(It.IsAny<Channel>()));
        _channelRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _connectionRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    [Fact]
    public async Task Handle_AccountInfoReturned_SavesAllThreeFieldsOnConnection()
    {
        SystemChannelTemplate template = BuildTemplate();
        SetupHappyPath(template);

        _apiClient
            .Setup(c => c.GetAccountInfoAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<Dictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountInfo
            {
                CustomerName = "Ромашка Маркет",
                LegalName = "ООО Ромашка",
                Inn = "7701234567",
            });

        Connection? savedConnection = null;
        _connectionRepo
            .Setup(r => r.Add(It.IsAny<Connection>()))
            .Callback<Connection>(c => savedConnection = c);

        CreateConnectionCommandHandler handler = CreateHandler();
        await handler.Handle(new CreateConnectionCommand
        {
            SystemChannelTemplateCode = "wildberries",
            ApiKey = "valid-key",
        }, CancellationToken.None);

        savedConnection.Should().NotBeNull();
        savedConnection!.CustomerName.Should().Be("Ромашка Маркет");
        savedConnection.LegalName.Should().Be("ООО Ромашка");
        savedConnection.Inn.Should().Be("7701234567");
    }

    [Fact]
    public async Task Handle_AccountInfoReturnsNull_ConnectionFieldsAreNull()
    {
        SystemChannelTemplate template = BuildTemplate();
        SetupHappyPath(template);

        _apiClient
            .Setup(c => c.GetAccountInfoAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<Dictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((AccountInfo?)null);

        Connection? savedConnection = null;
        _connectionRepo
            .Setup(r => r.Add(It.IsAny<Connection>()))
            .Callback<Connection>(c => savedConnection = c);

        CreateConnectionCommandHandler handler = CreateHandler();
        await handler.Handle(new CreateConnectionCommand
        {
            SystemChannelTemplateCode = "wildberries",
            ApiKey = "valid-key",
        }, CancellationToken.None);

        savedConnection.Should().NotBeNull();
        savedConnection!.CustomerName.Should().BeNull();
        savedConnection.LegalName.Should().BeNull();
        savedConnection.Inn.Should().BeNull();
    }

    [Fact]
    public async Task Handle_PartialAccountInfo_SavesOnlyPresentFields()
    {
        // Яндекс.Кит возвращает только CustomerName
        SystemChannelTemplate template = BuildTemplate("yandex.kit");
        SetupHappyPath(template);

        _apiClient
            .Setup(c => c.GetAccountInfoAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<Dictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountInfo
            {
                CustomerName = "my-store-slug",
                LegalName = null,
                Inn = null,
            });

        Connection? savedConnection = null;
        _connectionRepo
            .Setup(r => r.Add(It.IsAny<Connection>()))
            .Callback<Connection>(c => savedConnection = c);

        CreateConnectionCommandHandler handler = CreateHandler();
        await handler.Handle(new CreateConnectionCommand
        {
            SystemChannelTemplateCode = "yandex.kit",
            ApiKey = "yk-key",
        }, CancellationToken.None);

        savedConnection.Should().NotBeNull();
        savedConnection!.CustomerName.Should().Be("my-store-slug");
        savedConnection.LegalName.Should().BeNull();
        savedConnection.Inn.Should().BeNull();
    }

    [Fact]
    public async Task Handle_AccountInfoReturnsNull_ConnectionStillSaved()
    {
        // GetAccountInfoAsync — best-effort: null не должен блокировать создание подключения
        SystemChannelTemplate template = BuildTemplate();
        SetupHappyPath(template);

        _apiClient
            .Setup(c => c.GetAccountInfoAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<Dictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((AccountInfo?)null);

        Connection? savedConnection = null;
        _connectionRepo
            .Setup(r => r.Add(It.IsAny<Connection>()))
            .Callback<Connection>(c => savedConnection = c);

        CreateConnectionCommandHandler handler = CreateHandler();

        Func<Task> act = () => handler.Handle(new CreateConnectionCommand
        {
            SystemChannelTemplateCode = "wildberries",
            ApiKey = "valid-key",
        }, CancellationToken.None);

        await act.Should().NotThrowAsync();
        savedConnection.Should().NotBeNull();
        savedConnection!.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_AccountInfoCalledWithCorrectCredentials()
    {
        SystemChannelTemplate template = BuildTemplate("ozon");
        SetupHappyPath(template);

        _apiClient
            .Setup(c => c.GetAccountInfoAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<Dictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((AccountInfo?)null);

        _connectionRepo.Setup(r => r.Add(It.IsAny<Connection>()));

        Dictionary<string, string> settings = new Dictionary<string, string>
        {
            ["sellerId"] = "99999",
        };

        CreateConnectionCommandHandler handler = CreateHandler();
        await handler.Handle(new CreateConnectionCommand
        {
            SystemChannelTemplateCode = "ozon",
            ApiKey = "ozon-api-key",
            ApiSecret = null,
            Settings = settings,
        }, CancellationToken.None);

        // credentials должны совпадать с теми, что переданы в команду
        _apiClient.Verify(c => c.GetAccountInfoAsync(
            "ozon-api-key",
            null,
            settings,
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
