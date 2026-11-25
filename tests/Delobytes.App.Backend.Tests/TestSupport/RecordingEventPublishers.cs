using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Delobytes.App.Backend.Tests.TestSupport;

/// <summary>
/// Records the events a handler publishes. Every module declares its own
/// <c>IEventPublisher</c> with the same shape, so the recording body lives here once and the
/// per-module doubles below only bind it to their interface. Suites used to carry a private copy each.
/// </summary>
internal abstract class RecordingEventPublisherBase
{
    public List<object> Events { get; } = new List<object>();

    protected Task RecordAsync<TEvent>(TEvent @event)
        where TEvent : class
    {
        Events.Add(@event);

        return Task.CompletedTask;
    }
}

internal sealed class RecordingCatalogEventPublisher
    : RecordingEventPublisherBase, Delobytes.App.Backend.Catalog.Application.Interfaces.IEventPublisher
{
    public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken)
        where TEvent : class
    {
        return RecordAsync(@event);
    }
}

internal sealed class RecordingSalesEventPublisher
    : RecordingEventPublisherBase, Delobytes.App.Backend.Sales.Application.Interfaces.IEventPublisher
{
    public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken)
        where TEvent : class
    {
        return RecordAsync(@event);
    }
}

internal sealed class RecordingFinanceEventPublisher
    : RecordingEventPublisherBase, Delobytes.App.Backend.Finance.Application.Interfaces.IEventPublisher
{
    public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken)
        where TEvent : class
    {
        return RecordAsync(@event);
    }
}
