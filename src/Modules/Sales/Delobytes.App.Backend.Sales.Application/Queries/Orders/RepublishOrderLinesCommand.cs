using Delobytes.App.Backend.Contracts.Accounting;
using Delobytes.App.Backend.Contracts.Authorization;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Sales.Application.Interfaces;
using Delobytes.App.Backend.Sales.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Sales.Application.Models;
using Delobytes.App.Backend.Sales.Application.Options;
using Delobytes.App.Backend.Sales.Contracts.Events;
using MediatR;
using Microsoft.Extensions.Options;

namespace Delobytes.App.Backend.Sales.Application.Queries.Orders;

/// <summary>
/// Re-publishes the order line change facts for a bounded page of existing lines. Used for the
/// initial backfill and as the only recovery path when a change event was lost between Sales and
/// its consumers.
/// </summary>
public class RepublishOrderLinesCommand : IRequest<RepublishOrderLinesResponse>, IRequireRole
{
    /// <summary>Gets or sets the optional channel filter.</summary>
    public Guid? ChannelId { get; set; }

    /// <summary>Gets or sets the optional first order date, tenant-local (inclusive).</summary>
    public DateOnly? FromDate { get; set; }

    /// <summary>Gets or sets the optional last order date, tenant-local (inclusive).</summary>
    public DateOnly? ToDate { get; set; }

    /// <summary>Gets or sets the continuation token returned by the previous page.</summary>
    public string? ContinuationToken { get; set; }

    /// <summary>Gets or sets the requested page size, capped by the handler.</summary>
    public int? MaxLines { get; set; }

    /// <inheritdoc/>
    public Role[] AllowedRoles => new[] { Role.Administrator };
}

/// <summary>
/// Handles <see cref="RepublishOrderLinesCommand"/>.
/// </summary>
public class RepublishOrderLinesCommandHandler : IRequestHandler<RepublishOrderLinesCommand, RepublishOrderLinesResponse>
{
    private const int DefaultMaxLines = 5000;
    private const int HardMaxLines = 5000;
    private const int DefaultChunkSize = 500;

    private readonly IOrderLineRepublishRepository _repository;
    private readonly IEventPublisher _eventPublisher;
    private readonly ITenantContext _tenantContext;
    private readonly ITenantTimeService _tenantTimeService;
    private readonly IOptions<SalesImportOptions> _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="RepublishOrderLinesCommandHandler"/> class.
    /// </summary>
    /// <param name="repository">Paged order line source.</param>
    /// <param name="eventPublisher">Event publisher.</param>
    /// <param name="tenantContext">Tenant context.</param>
    /// <param name="tenantTimeService">Tenant time service, used to place the date bounds.</param>
    /// <param name="options">Import tuning.</param>
    public RepublishOrderLinesCommandHandler(
        IOrderLineRepublishRepository repository,
        IEventPublisher eventPublisher,
        ITenantContext tenantContext,
        ITenantTimeService tenantTimeService,
        IOptions<SalesImportOptions> options)
    {
        _repository = repository;
        _eventPublisher = eventPublisher;
        _tenantContext = tenantContext;
        _tenantTimeService = tenantTimeService;
        _options = options;
    }

    /// <inheritdoc/>
    public async Task<RepublishOrderLinesResponse> Handle(
        RepublishOrderLinesCommand request,
        CancellationToken cancellationToken)
    {
        Guid tenantId = _tenantContext.TenantId
            ?? throw new UnauthorizedAccessException("Пользователь не аутентифицирован.");

        int limit = Math.Clamp(request.MaxLines ?? DefaultMaxLines, 1, HardMaxLines);

        TimeZoneInfo timeZone = await _tenantTimeService.GetTimeZoneAsync(tenantId, cancellationToken);

        DateTimeOffset? fromUtc = request.FromDate.HasValue
            ? TenantTimeZones.StartOfDayUtc(request.FromDate.Value, timeZone)
            : null;

        DateTimeOffset? toUtcExclusive = request.ToDate.HasValue
            ? TenantTimeZones.StartOfDayUtc(request.ToDate.Value.AddDays(1), timeZone)
            : null;

        IReadOnlyList<RepublishOrderLine> rows = await _repository.GetPageAsync(
            request.ChannelId,
            fromUtc,
            toUtcExclusive,
            request.ContinuationToken,
            limit,
            cancellationToken);

        int chunkSize = _options.Value.OrderLinesChangedChunkSize > 0
            ? _options.Value.OrderLinesChangedChunkSize
            : DefaultChunkSize;

        int chunks = 0;

        for (int offset = 0; offset < rows.Count; offset += chunkSize)
        {
            List<ChangedOrderLine> chunk = rows
                .Skip(offset)
                .Take(chunkSize)
                .Select(row => new ChangedOrderLine
                {
                    OrderLineId = row.OrderLineId,
                    OrderId = row.OrderId,
                    ChannelId = row.ChannelId,
                    ExternalOrderId = row.ExternalOrderId,
                    ExternalLineId = row.ExternalLineId,
                    ExternalProductId = row.ExternalProductId,
                    ChannelProductId = row.ChannelProductId,
                    Quantity = row.Quantity,
                    OrderDate = row.OrderDate,
                    Currency = row.Currency,
                    IsCancelled = row.IsCancelled,
                    IsNew = false,
                    CostRelevantChange = true,
                })
                .ToList();

            await _eventPublisher.PublishAsync(
                new OrderLinesChangedEvent { Lines = chunk },
                cancellationToken);

            chunks++;
        }

        string? nextToken = rows.Count == limit
            ? rows[rows.Count - 1].OrderLineId.ToString()
            : null;

        return new RepublishOrderLinesResponse
        {
            PublishedLines = rows.Count,
            ChunksPublished = chunks,
            NextContinuationToken = nextToken,
        };
    }
}
