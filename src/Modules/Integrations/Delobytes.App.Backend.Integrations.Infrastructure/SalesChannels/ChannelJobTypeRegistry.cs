using System.Linq;
using Delobytes.App.Backend.Integrations.Application.Jobs;
using Delobytes.App.Backend.Integrations.Application.SalesChannels;
using Delobytes.App.Backend.Integrations.Domain.Enums;

namespace Delobytes.App.Backend.Integrations.Infrastructure.SalesChannels;

/// <summary>
/// Unifies the two levels of channel knowledge: the descriptor declares which import kinds a
/// channel supports, and the module owns the concrete job types behind them. The constructor
/// validates the pair, so a channel that declares a capability without serving it — or serves a
/// job type it never declared — fails on startup instead of silently never dispatching.
/// </summary>
public sealed class ChannelJobTypeRegistry : IChannelJobTypeRegistry
{
    private readonly IReadOnlyDictionary<string, ChannelModule> _modulesByCode;
    private readonly IReadOnlyDictionary<JobType, ChannelJobKind> _kinds;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChannelJobTypeRegistry"/> class.
    /// </summary>
    /// <param name="modules">Registered channel modules.</param>
    public ChannelJobTypeRegistry(IEnumerable<ChannelModule> modules)
    {
        Dictionary<string, ChannelModule> modulesByCode = new Dictionary<string, ChannelModule>(StringComparer.OrdinalIgnoreCase);
        Dictionary<JobType, ChannelJobKind> kinds = new Dictionary<JobType, ChannelJobKind>();

        foreach (ChannelModule module in modules)
        {
            ChannelDescriptor descriptor = module.Descriptor;

            modulesByCode.Add(descriptor.Code, module);

            HashSet<ChannelJobKind> servedKinds = new HashSet<ChannelJobKind>();

            foreach ((JobType jobType, ChannelJobKind kind) moduleJobType in module.JobTypes)
            {
                servedKinds.Add(moduleJobType.kind);

                if (!kinds.TryAdd(moduleJobType.jobType, moduleJobType.kind))
                {
                    throw new InvalidOperationException(
                        $"Job type {moduleJobType.jobType} is served by more than one channel module.");
                }
            }

            foreach (ChannelJobKind kind in descriptor.SupportedJobKinds)
            {
                if (!servedKinds.Contains(kind))
                {
                    throw new InvalidOperationException(
                        $"Channel '{descriptor.Code}' declares {kind} support but its module serves no job type of that kind.");
                }
            }

            foreach (ChannelJobKind kind in servedKinds)
            {
                if (!descriptor.SupportedJobKinds.Contains(kind))
                {
                    throw new InvalidOperationException(
                        $"Channel '{descriptor.Code}' serves a {kind} job type but does not declare that capability.");
                }
            }
        }

        _modulesByCode = modulesByCode;
        _kinds = kinds;
    }

    /// <inheritdoc/>
    public ChannelJobKind? ResolveKind(JobType jobType)
    {
        return _kinds.TryGetValue(jobType, out ChannelJobKind kind) ? kind : null;
    }

    /// <inheritdoc/>
    public bool IsServedByChannel(string? channelCode, JobType jobType)
    {
        return TryGetModule(channelCode, out ChannelModule? module) && Serves(module, jobType);
    }

    /// <inheritdoc/>
    public IReadOnlyList<JobType> GetJobTypes(string? channelCode, ChannelJobKind kind)
    {
        if (!TryGetModule(channelCode, out ChannelModule? module))
        {
            return Array.Empty<JobType>();
        }

        return module.JobTypes
            .Where(moduleJobType => moduleJobType.Kind == kind)
            .Select(moduleJobType => moduleJobType.JobType)
            .ToList();
    }

    /// <inheritdoc/>
    public string? GetRateLimitMethod(string? channelCode, JobType jobType)
    {
        return TryGetModule(channelCode, out ChannelModule? module) && Serves(module, jobType)
            ? module.GetRateLimitMethod(jobType)
            : null;
    }

    private bool TryGetModule(string? channelCode, out ChannelModule module)
    {
        module = null!;

        return !string.IsNullOrWhiteSpace(channelCode)
            && _modulesByCode.TryGetValue(channelCode, out module);
    }

    private static bool Serves(ChannelModule module, JobType jobType)
    {
        return module.JobTypes.Any(moduleJobType => moduleJobType.JobType == jobType);
    }
}
