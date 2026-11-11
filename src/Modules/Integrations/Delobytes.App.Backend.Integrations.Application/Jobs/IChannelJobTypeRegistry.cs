using Delobytes.App.Backend.Integrations.Domain.Enums;

namespace Delobytes.App.Backend.Integrations.Application.Jobs;

/// <summary>
/// What dispatching a job run means: which orchestration the worker must trigger, independent of
/// the channel that produced the run.
/// </summary>
public enum ChannelJobKind
{
    ProductsImport = 0,
    OrdersImport = 1,
}

/// <summary>
/// Answers what a job run means and which channel serves it, from the registered channel modules.
/// Callers therefore never enumerate channel-specific job type values themselves.
/// </summary>
public interface IChannelJobTypeRegistry
{
    /// <summary>
    /// Resolves the orchestration a job type triggers, or <c>null</c> when no registered channel
    /// serves it.
    /// </summary>
    public ChannelJobKind? ResolveKind(JobType jobType);

    /// <summary>
    /// True when the channel's module serves the given job type.
    /// </summary>
    public bool IsServedByChannel(string? channelCode, JobType jobType);

    /// <summary>
    /// Returns the channel's job types of the given kind, in the order the module declared them.
    /// An unknown or blank channel code yields an empty list.
    /// </summary>
    public IReadOnlyList<JobType> GetJobTypes(string? channelCode, ChannelJobKind kind);

    /// <summary>
    /// Returns the rate-limit method the channel paces the job type on, or <c>null</c> when the
    /// channel reports none.
    /// </summary>
    public string? GetRateLimitMethod(string? channelCode, JobType jobType);
}
