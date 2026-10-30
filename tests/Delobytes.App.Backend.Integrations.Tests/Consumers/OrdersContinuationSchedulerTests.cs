using System;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers.OrdersImport;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.Consumers;

public class OrdersContinuationSchedulerTests
{
    private readonly FakeTimeProvider _timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 10, 21, 9, 0, 0, TimeSpan.Zero));
    private readonly Mock<IJobRunRepository> _jobRunRepo = new();

    private OrdersContinuationScheduler CreateScheduler()
    {
        return new OrdersContinuationScheduler(_jobRunRepo.Object, _timeProvider);
    }

    private static JobRun BuildCurrentRun()
    {
        return new JobRun
        {
            Id = Guid.NewGuid(),
            ConnectionId = Guid.NewGuid(),
            JobType = JobType.OrdersImport,
            Status = JobStatus.Running,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
        };
    }

    [Fact]
    public async Task ScheduleAsync_NoPendingRun_CreatesPendingSuccessorAtResumeInstant()
    {
        JobRun current = BuildCurrentRun();
        DateTimeOffset resumeAt = _timeProvider.GetUtcNow().AddHours(3);

        _jobRunRepo
            .Setup(r => r.HasPendingJobAsync(current.ConnectionId, JobType.OrdersImport, current.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _jobRunRepo
            .Setup(r => r.CreateAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((JobRun created, CancellationToken _) => created);

        JobRun? continuation = await CreateScheduler().ScheduleAsync(current, resumeAt, CancellationToken.None);

        continuation.Should().NotBeNull();
        continuation!.Id.Should().NotBe(current.Id);
        continuation.ConnectionId.Should().Be(current.ConnectionId);
        continuation.JobType.Should().Be(JobType.OrdersImport);
        continuation.Status.Should().Be(JobStatus.Pending);
        continuation.NotBefore.Should().Be(resumeAt);
        continuation.CreatedAt.Should().Be(_timeProvider.GetUtcNow());
        continuation.StartedAt.Should().BeNull();

        _jobRunRepo.Verify(
            r => r.CreateAsync(It.Is<JobRun>(next => next.Id == continuation.Id), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ScheduleAsync_PendingRunExists_ReturnsNullAndCreatesNothing()
    {
        JobRun current = BuildCurrentRun();

        _jobRunRepo
            .Setup(r => r.HasPendingJobAsync(current.ConnectionId, JobType.OrdersImport, current.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        JobRun? continuation = await CreateScheduler().ScheduleAsync(
            current, _timeProvider.GetUtcNow().AddHours(1), CancellationToken.None);

        // A redelivered message must not stack a second continuation on an existing pending one.
        continuation.Should().BeNull();
        _jobRunRepo.Verify(r => r.CreateAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ScheduleAsync_ExcludesCurrentRunFromThePendingCheck()
    {
        JobRun current = BuildCurrentRun();

        _jobRunRepo
            .Setup(r => r.HasPendingJobAsync(It.IsAny<Guid>(), It.IsAny<JobType>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _jobRunRepo
            .Setup(r => r.CreateAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((JobRun created, CancellationToken _) => created);

        await CreateScheduler().ScheduleAsync(current, _timeProvider.GetUtcNow(), CancellationToken.None);

        // A run that is itself still Pending when the check happens would otherwise look like its
        // own continuation and block the retry forever.
        _jobRunRepo.Verify(
            r => r.HasPendingJobAsync(current.ConnectionId, JobType.OrdersImport, current.Id, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
