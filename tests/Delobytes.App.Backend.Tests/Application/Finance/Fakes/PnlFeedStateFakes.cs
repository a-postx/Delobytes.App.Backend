using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Finance.Application.Interfaces;
using Delobytes.App.Backend.Finance.Application.Models;
using Delobytes.App.Backend.Finance.Domain.Entities;

namespace Delobytes.App.Backend.Tests.Application.Finance.Fakes;

internal sealed class InMemoryPnlFeedStateStore : IPnlFeedStateStore
{
    public List<PnlFeedCheckpoint> Recorded { get; } = new List<PnlFeedCheckpoint>();

    public List<PnlFeedState> States { get; } = new List<PnlFeedState>();

    public Task<PnlFeedState?> LoadAsync(Guid channelId, CancellationToken cancellationToken)
    {
        PnlFeedState? state = States.FirstOrDefault(s => s.ChannelId == channelId);

        return Task.FromResult(state);
    }

    public Task<IReadOnlyList<PnlFeedState>> LoadAllAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<PnlFeedState> states = States.ToList();

        return Task.FromResult(states);
    }

    public Task RecordBatchAsync(PnlFeedCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        Recorded.Add(checkpoint);

        return Task.CompletedTask;
    }
}
