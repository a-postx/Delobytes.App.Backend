using System;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Delobytes.App.Backend.Integrations.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.Services;

public class JobRunLifecycleTests
{
    private readonly FakeTimeProvider _timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 10, 21, 9, 0, 0, TimeSpan.Zero));
    private readonly Mock<IJobRunRepository> _jobRunRepo = new();

    private JobRunLifecycle CreateLifecycle()
    {
        return new JobRunLifecycle(_jobRunRepo.Object, _timeProvider);
    }

    private static JobRun BuildJobRun(JobStatus status = JobStatus.Pending)
    {
        return new JobRun
        {
            Id = Guid.NewGuid(),
            ConnectionId = Guid.NewGuid(),
            JobType = JobType.OrdersImport,
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
        };
    }

    [Fact]
    public async Task TryStartAsync_PendingJob_RunsAndClearsNotBefore()
    {
        JobRun jobRun = BuildJobRun();
        jobRun.NotBefore = _timeProvider.GetUtcNow().AddHours(1);
        DateTimeOffset now = _timeProvider.GetUtcNow();

        await CreateLifecycle().StartAsync(jobRun, CancellationToken.None);

        jobRun.Status.Should().Be(JobStatus.Running);
        jobRun.StartedAt.Should().Be(now);
        jobRun.NotBefore.Should().BeNull();
        _jobRunRepo.Verify(r => r.UpdateAsync(jobRun, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TryStartAsync_AlreadyRunningJob_LeavesItUntouched()
    {
        JobRun jobRun = BuildJobRun(JobStatus.Running);

        await CreateLifecycle().StartAsync(jobRun, CancellationToken.None);

        // A redelivered message must not reset StartedAt: the run began when it first began.
        jobRun.StartedAt.Should().BeNull();
        _jobRunRepo.Verify(r => r.UpdateAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(JobStatus.Success)]
    [InlineData(JobStatus.Failed)]
    [InlineData(JobStatus.Cancelled)]
    public async Task TryStartAsync_TerminalJob_LeavesItUntouched(JobStatus status)
    {
        JobRun jobRun = BuildJobRun(status);

        await CreateLifecycle().StartAsync(jobRun, CancellationToken.None);

        jobRun.Status.Should().Be(status);
        _jobRunRepo.Verify(r => r.UpdateAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task FailAsync_WritesTerminalStatusAndError()
    {
        JobRun jobRun = BuildJobRun(JobStatus.Running);
        DateTimeOffset now = _timeProvider.GetUtcNow();

        await CreateLifecycle().FailAsync(jobRun, "connection is inactive", CancellationToken.None);

        jobRun.Status.Should().Be(JobStatus.Failed);
        jobRun.Error.Should().Be("connection is inactive");
        jobRun.CompletedAt.Should().Be(now);
        _jobRunRepo.Verify(r => r.UpdateAsync(jobRun, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FailAsync_CancelledToken_StillPersistsTheFailure()
    {
        JobRun jobRun = BuildJobRun(JobStatus.Running);

        using CancellationTokenSource cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await CreateLifecycle().FailAsync(jobRun, "boom", cancellation.Token);

        jobRun.Status.Should().Be(JobStatus.Failed);

        // The consumer's own token may already be cancelled when the failure is written; the failure
        // still has to reach the database, so the write must not carry the cancelled token.
        _jobRunRepo.Verify(
            r => r.UpdateAsync(jobRun, It.Is<CancellationToken>(token => !token.CanBeCanceled)),
            Times.Once);
    }
}
