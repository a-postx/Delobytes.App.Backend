using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Finance.Application.Interfaces;
using Delobytes.App.Backend.Finance.Application.ReadModels;
using Delobytes.App.Backend.Finance.Domain.Entities;
using Delobytes.App.Backend.Finance.Domain.Enums;
using MediatR;

namespace Delobytes.App.Backend.Finance.Application.Queries;

/// <summary>One row of the expectation queue.</summary>
public sealed class PnlExpectationQueueRowView
{
    public Guid Id { get; set; }

    public Guid OrderLineId { get; set; }

    public PnlEntryType ExpectedType { get; set; }

    public PnlSource Source { get; set; }

    public DateTimeOffset ExpectedBy { get; set; }

    public PnlExpectationState State { get; set; }

    public decimal? AmountEstimate { get; set; }

    public string Currency { get; set; } = default!;

    public DateTimeOffset? ClosedAt { get; set; }

    public Guid? ClosedByUserId { get; set; }

    public string? ClosureReason { get; set; }

    /// <summary>Gets or sets whole days past the deadline; negative while the window is open.</summary>
    public int DaysOverdue { get; set; }

    /// <summary>Gets or sets the aging bucket, or <c>closed</c> for a resolved expectation.</summary>
    public string AgingBucket { get; set; } = default!;
}

/// <summary>Response of the expectation queue.</summary>
public sealed class PnlExpectationQueueResponse
{
    public List<PnlExpectationQueueRowView> Rows { get; set; } = new();

    public string? ContinuationToken { get; set; }
}

/// <summary>
/// Query of the expected-document queue: what the channel owes and has not reported yet.
/// </summary>
public sealed class GetPnlExpectationsQuery : IRequest<PnlExpectationQueueResponse>
{
    public PnlExpectationState? State { get; set; }

    public DateTimeOffset? DueFrom { get; set; }

    public DateTimeOffset? DueTo { get; set; }

    public Guid? OrderLineId { get; set; }

    public int Take { get; set; } = 100;

    public string? ContinuationToken { get; set; }
}

/// <summary>Handles <see cref="GetPnlExpectationsQuery"/>.</summary>
public sealed class GetPnlExpectationsQueryHandler
    : IRequestHandler<GetPnlExpectationsQuery, PnlExpectationQueueResponse>
{
    private readonly IPnlExpectationStore _store;
    private readonly ITenantContext _tenantContext;
    private readonly TimeProvider _timeProvider;

    public GetPnlExpectationsQueryHandler(
        IPnlExpectationStore store,
        ITenantContext tenantContext,
        TimeProvider timeProvider)
    {
        _store = store;
        _tenantContext = tenantContext;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc/>
    public async Task<PnlExpectationQueueResponse> Handle(
        GetPnlExpectationsQuery request,
        CancellationToken cancellationToken)
    {
        if (_tenantContext.TenantId is null)
        {
            throw new UnauthorizedAccessException("Пользователь не аутентифицирован.");
        }

        int take = Math.Clamp(request.Take, 1, 500);

        PnlExpectationPage page = await _store.GetPageAsync(
            new PnlExpectationFilter
            {
                State = request.State,
                DueFrom = request.DueFrom,
                DueTo = request.DueTo,
                OrderLineId = request.OrderLineId,
            },
            take,
            request.ContinuationToken,
            cancellationToken);

        DateTimeOffset now = _timeProvider.GetUtcNow();

        List<PnlExpectationQueueRowView> rows = page.Rows
            .Select(row => ToView(row, now))
            .ToList();

        return new PnlExpectationQueueResponse
        {
            Rows = rows,
            ContinuationToken = page.NextToken,
        };
    }

    private static PnlExpectationQueueRowView ToView(PnlLineExpectation expectation, DateTimeOffset now)
    {
        int daysOverdue = PnlExpectationAging.DaysOverdue(expectation.ExpectedBy, now);

        return new PnlExpectationQueueRowView
        {
            Id = expectation.Id,
            OrderLineId = expectation.OrderLineId,
            ExpectedType = expectation.ExpectedType,
            Source = expectation.Source,
            ExpectedBy = expectation.ExpectedBy,
            State = expectation.State,
            AmountEstimate = expectation.AmountEstimate,
            Currency = expectation.Currency,
            ClosedAt = expectation.ClosedAt,
            ClosedByUserId = expectation.ClosedByUserId,
            ClosureReason = expectation.ClosureReason,
            DaysOverdue = daysOverdue,
            AgingBucket = PnlExpectationAging.BucketOf(expectation.State, daysOverdue),
        };
    }
}
