using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Application.Options;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Application.Orders;
using Moq;

namespace Delobytes.App.Backend.Integrations.Tests.Consumers;

/// <summary>
/// Builds a complete <see cref="OrdersImportContext"/> with valid defaults so a test can override
/// only the property it exercises.
/// </summary>
internal static class OrdersImportContextBuilder
{
    public static OrdersImportContext Build(
        JobRun? jobRun = null,
        Connection? connection = null,
        IOrdersSource? ordersSource = null,
        OrdersBackfillState? backfillState = null,
        OrdersImportTuning? tuning = null,
        DateTimeOffset? deadline = null,
        DateTimeOffset? windowTo = null,
        bool isBackfill = false,
        OrdersSchema? schema = null)
    {
        JobRun effectiveJobRun = jobRun ?? new JobRun
        {
            Id = Guid.NewGuid(),
            ConnectionId = Guid.NewGuid(),
        };

        Connection effectiveConnection = connection ?? new Connection
        {
            Id = effectiveJobRun.ConnectionId,
            ChannelId = Guid.NewGuid(),
        };

        return new OrdersImportContext
        {
            JobRun = effectiveJobRun,
            Connection = effectiveConnection,
            OrdersSource = ordersSource ?? Mock.Of<IOrdersSource>(),
            BackfillState = backfillState ?? new OrdersBackfillState(),
            Tuning = tuning ?? TestTunings.Wildberries(),
            Deadline = deadline ?? DateTimeOffset.UtcNow.AddMinutes(10),
            WindowTo = windowTo ?? DateTimeOffset.UtcNow,
            IsBackfill = isBackfill,
            Schema = schema,
        };
    }
}
