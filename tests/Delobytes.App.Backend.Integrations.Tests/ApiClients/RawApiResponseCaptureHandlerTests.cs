using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients;
using Delobytes.App.Backend.Integrations.Infrastructure.Persistence;
using Delobytes.App.Backend.Integrations.Infrastructure.Persistence.Repositories;
using Delobytes.App.Backend.Integrations.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RichardSzalay.MockHttp;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.ApiClients;

/// <summary>
/// End-to-end verification of BUG-2 (R11): a real HttpClient pipeline with
/// RawApiResponseCaptureHandler attached, backed by a real IntegrationsDbContext, actually
/// produces a row in RawApiResponses when a JobRun id is set on the ambient context - closing the
/// gap the unit-level handler tests cannot see (the tenant-scoping path to SaveChangesAsync).
/// </summary>
public class RawApiResponseCaptureHandlerTests
{
    private static DbContextOptions<IntegrationsDbContext> BuildOptions()
    {
        return new DbContextOptionsBuilder<IntegrationsDbContext>()
            .UseInMemoryDatabase("raw-api-response-capture-" + Guid.NewGuid())
            .Options;
    }

    private static Mock<ITenantContext> TenantMock(Guid tenantId)
    {
        Mock<ITenantContext> mock = new Mock<ITenantContext>();
        mock.Setup(t => t.TenantId).Returns(tenantId);
        return mock;
    }

    private static SystemChannelTemplate BuildTemplate()
    {
        return new SystemChannelTemplate
        {
            Id = Guid.NewGuid(),
            Code = "wildberries",
            DisplayName = "Wildberries",
            ApiBaseUrl = "https://suppliers-api.wildberries.ru",
            ApiVersion = "v3",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    private static Connection BuildConnection(SystemChannelTemplate template)
    {
        return new Connection
        {
            Id = Guid.NewGuid(),
            ChannelId = Guid.NewGuid(),
            SystemChannelTemplateId = template.Id,
            Name = "Test WB",
            ApiKey = "test-key",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    private static JobRun BuildJobRun(Connection connection)
    {
        return new JobRun
        {
            Id = Guid.NewGuid(),
            ConnectionId = connection.Id,
            JobType = JobType.OrdersImport,
            Status = JobStatus.Running,
            CreatedAt = DateTimeOffset.UtcNow,
            StartedAt = DateTimeOffset.UtcNow,
        };
    }

    [Fact]
    public async Task SendAsync_JobRunIdSetOnAmbientContext_PersistsRowInRawApiResponses()
    {
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();
        Guid tenantId = Guid.NewGuid();
        Mock<ITenantContext> tenantMock = TenantMock(tenantId);

        SystemChannelTemplate template = BuildTemplate();
        Connection connection = BuildConnection(template);
        JobRun jobRun = BuildJobRun(connection);

        using (IntegrationsDbContext seedContext = new IntegrationsDbContext(options, tenantMock.Object))
        {
            seedContext.SystemChannelTemplates.Add(template);
            seedContext.Connections.Add(connection);
            seedContext.JobRuns.Add(jobRun);
            await seedContext.SaveChangesAsync();
        }

        JobRunExecutionContext jobRunExecutionContext = new JobRunExecutionContext();
        jobRunExecutionContext.SetCurrent(jobRun.Id);

        try
        {
            string responseJson = """[{"srid":"SRID-1"}]""";

            MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
            mockHttp
                .When(HttpMethod.Get, "https://statistics-api.wildberries.ru/api/v1/supplier/orders*")
                .Respond("application/json", responseJson);

            using IntegrationsDbContext captureContext = new IntegrationsDbContext(options, tenantMock.Object);
            RawApiResponseRepository repository = new RawApiResponseRepository(captureContext);

            RawApiResponseCaptureHandler handler = new RawApiResponseCaptureHandler(
                jobRunExecutionContext,
                repository,
                NullLogger<RawApiResponseCaptureHandler>.Instance)
            {
                InnerHandler = mockHttp,
            };

            using HttpClient client = new HttpClient(handler);
            using HttpRequestMessage request = new HttpRequestMessage(
                HttpMethod.Get,
                "https://statistics-api.wildberries.ru/api/v1/supplier/orders?dateFrom=2024-01-01&flag=0");

            using HttpResponseMessage response = await client.SendAsync(request, CancellationToken.None);

            response.IsSuccessStatusCode.Should().BeTrue();

            using IntegrationsDbContext readContext = new IntegrationsDbContext(options, tenantMock.Object);

            RawApiResponse captured = await readContext.RawApiResponses.SingleAsync();

            captured.JobRunId.Should().Be(jobRun.Id);
            captured.Endpoint.Should().Contain("/api/v1/supplier/orders");
            captured.ResponsePayload.Should().Be(responseJson);
            captured.HttpStatusCode.Should().Be(200);

            // EF.Property only exists inside a LINQ query - it has no meaning against an
            // already-materialised instance - so the shadow property is read by projection.
            Guid? stampedTenantId = await readContext.RawApiResponses
                .Where(r => r.Id == captured.Id)
                .Select(r => EF.Property<Guid?>(r, "TenantId"))
                .SingleAsync();

            stampedTenantId.Should().Be(tenantId, "the capture handler runs across a different DI scope than the consumer, and R11 hinges on TenantId still resolving there");
        }
        finally
        {
            jobRunExecutionContext.Clear();
        }
    }

    [Fact]
    public async Task SendAsync_NoJobRunIdOnAmbientContext_PassesThroughWithoutPersistingAnything()
    {
        DbContextOptions<IntegrationsDbContext> options = BuildOptions();
        Mock<ITenantContext> tenantMock = TenantMock(Guid.NewGuid());

        JobRunExecutionContext jobRunExecutionContext = new JobRunExecutionContext();

        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp
            .When(HttpMethod.Get, "https://common-api.wildberries.ru/api/v1/seller-info")
            .Respond("application/json", """{"name":"Test"}""");

        using IntegrationsDbContext captureContext = new IntegrationsDbContext(options, tenantMock.Object);
        RawApiResponseRepository repository = new RawApiResponseRepository(captureContext);

        RawApiResponseCaptureHandler handler = new RawApiResponseCaptureHandler(
            jobRunExecutionContext,
            repository,
            NullLogger<RawApiResponseCaptureHandler>.Instance)
        {
            InnerHandler = mockHttp,
        };

        using HttpClient client = new HttpClient(handler);
        using HttpRequestMessage request = new HttpRequestMessage(
            HttpMethod.Get,
            "https://common-api.wildberries.ru/api/v1/seller-info");

        using HttpResponseMessage response = await client.SendAsync(request, CancellationToken.None);

        response.IsSuccessStatusCode.Should().BeTrue();

        using IntegrationsDbContext readContext = new IntegrationsDbContext(options, tenantMock.Object);

        List<RawApiResponse> rows = await readContext.RawApiResponses.ToListAsync();

        rows.Should().BeEmpty("a call made with no JobRun id ambient has nothing valid to attribute an audit row to");
    }
}
