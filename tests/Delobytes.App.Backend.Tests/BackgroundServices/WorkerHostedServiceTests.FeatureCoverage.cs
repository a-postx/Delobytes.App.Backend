using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.BackgroundServices;
using Delobytes.App.Backend.Contracts.Accounting;
using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Events;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Options;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Delobytes.App.Backend.Services;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;


namespace Delobytes.App.Backend.Tests.BackgroundServices;

public partial class WorkerHostedServiceTests
{
    [Fact]
    public async Task Tick_DeferredPending_IsDispatchedOnlyWhenRepositoryReportsItDue()
    {
        JobRun pending = BuildJobRun(JobType.OrdersImport);
        pending.NotBefore = DateTimeOffset.UtcNow.AddHours(3);
        DateTimeOffset observedNow = default;
        _jobRunRepositoryMock.Setup(r => r.GetPendingJobsAsync(_tenantId, It.IsAny<int>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .Returns((Guid tenantId, int limit, DateTimeOffset now, CancellationToken token) =>
            {
                observedNow = now;
                return Task.FromResult(pending.NotBefore <= now ? new List<JobRun> { pending } : new List<JobRun>());
            });
        WorkerHostedService worker = BuildWorker();
        DateTimeOffset before = DateTimeOffset.UtcNow;

        await RunSingleTickAsync(worker);

        observedNow.Should().BeOnOrAfter(before);
        observedNow.Should().BeOnOrBefore(DateTimeOffset.UtcNow);
        _eventPublisherMock.Verify(p => p.PublishAsync(It.IsAny<OrdersImportRequestedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        pending.NotBefore = observedNow;

        await RunSingleTickAsync(worker);

        _eventPublisherMock.Verify(p => p.PublishAsync(It.Is<OrdersImportRequestedEvent>(e => e.JobRunId == pending.Id), It.IsAny<CancellationToken>()), Times.Once);
    }
}
