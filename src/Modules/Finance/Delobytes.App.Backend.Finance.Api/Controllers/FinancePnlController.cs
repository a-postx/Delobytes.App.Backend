using Delobytes.App.Backend.Finance.Application.Queries;
using Delobytes.App.Backend.Finance.Application.Ranking;
using Delobytes.App.Backend.Finance.Application.ReadModels;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Delobytes.App.Backend.Finance.Api.Controllers;

/// <summary>
/// Read endpoints of the P&amp;L model.
/// </summary>
[ApiController]
[Route("api/finance")]
[Authorize]
public class FinancePnlController : ControllerBase
{
    private readonly IMediator _mediator;

    /// <summary>
    /// Initializes a new instance of the <see cref="FinancePnlController"/> class.
    /// </summary>
    public FinancePnlController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Returns P&amp;L lines with filters, sorting and keyset paging. The page is ordered by the
    /// requested key, newest period first by default; a continuation token only continues the
    /// ordering that produced it.
    /// </summary>
    [HttpGet("pnl-lines")]
    [ProducesResponseType(typeof(PnlLinesResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<PnlLinesResponse>> GetLines(
        [FromQuery] Guid? channelId,
        [FromQuery] Guid? productId,
        [FromQuery] Guid[]? productIds,
        [FromQuery] Guid? channelProductId,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] string? state,
        [FromQuery] string? completeness,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDir,
        [FromQuery] int take,
        [FromQuery] string? continuationToken,
        CancellationToken cancellationToken)
    {
        ValidateCompleteness(completeness);

        PnlLineSortKey parsedSortBy = PnlLineSortKey.Date;

        if (sortBy != null
            && (!Enum.TryParse(sortBy, ignoreCase: true, out parsedSortBy)
                || !Enum.IsDefined(parsedSortBy)))
        {
            throw new ValidationException(
                new[]
                {
                    new ValidationFailure(nameof(sortBy), "Сортировка должна быть одной из: date, margin."),
                });
        }

        bool descending = true;

        if (sortDir != null)
        {
            if (!string.Equals(sortDir, "asc", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase))
            {
                throw new ValidationException(
                    new[]
                    {
                        new ValidationFailure(nameof(sortDir), "Направление сортировки должно быть asc или desc."),
                    });
            }

            descending = !string.Equals(sortDir, "asc", StringComparison.OrdinalIgnoreCase);
        }

        PnlLinesResponse response = await _mediator.Send(
            new GetPnlLinesQuery
            {
                Filter = new PnlQueryFilter
                {
                    ChannelId = channelId,
                    ProductId = productId,
                    ProductIds = productIds,
                    ChannelProductId = channelProductId,
                    From = from,
                    To = to,
                    State = state,
                    Completeness = completeness,
                },
                Sort = new PnlLineSort { Key = parsedSortBy, Descending = descending },
                Take = take <= 0 ? 100 : take,
                ContinuationToken = continuationToken,
            },
            cancellationToken);

        return Ok(response);
    }

    /// <summary>
    /// Returns the summary by product, by channel or with no dimension, and by period. A granularity
    /// of <c>total</c> reports the whole requested range as one period; with a <c>groupBy</c> of
    /// <c>none</c> the whole range collapses into a single row.
    /// </summary>
    [HttpGet("pnl-summary")]
    [ProducesResponseType(typeof(PnlSummaryResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<PnlSummaryResponse>> GetSummary(
        [FromQuery] Guid? channelId,
        [FromQuery] Guid? productId,
        [FromQuery] Guid[]? productIds,
        [FromQuery] Guid? channelProductId,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] string? granularity,
        [FromQuery] string? groupBy,
        [FromQuery] string? completeness,
        CancellationToken cancellationToken)
    {
        PnlPeriodGranularity parsedGranularity = PnlPeriodGranularity.Day;

        if (granularity != null)
        {
            if (!Enum.TryParse(granularity, ignoreCase: true, out parsedGranularity)
                || !Enum.IsDefined(parsedGranularity))
            {
                throw new ValidationException(
                    new[]
                    {
                        new ValidationFailure(
                            nameof(granularity),
                            "Гранулярность должна быть одной из: day, week, month, total."),
                    });
            }
        }

        PnlSummaryGroupBy parsedGroupBy = PnlSummaryGroupBy.Product;

        if (groupBy != null)
        {
            if (!Enum.TryParse(groupBy, ignoreCase: true, out parsedGroupBy)
                || !Enum.IsDefined(parsedGroupBy))
            {
                throw new ValidationException(
                    new[]
                    {
                        new ValidationFailure(
                            nameof(groupBy),
                            "Группировка должна быть одной из: product, channel, none."),
                    });
            }
        }

        ValidateCompleteness(completeness);

        PnlSummaryResponse response = await _mediator.Send(
            new GetPnlSummaryQuery
            {
                Filter = new PnlQueryFilter
                {
                    ChannelId = channelId,
                    ProductId = productId,
                    ProductIds = productIds,
                    ChannelProductId = channelProductId,
                    From = from,
                    To = to,
                    Completeness = completeness,
                },
                Granularity = parsedGranularity,
                GroupBy = parsedGroupBy,
            },
            cancellationToken);

        return Ok(response);
    }

    /// <summary>
    /// Returns the whole overview of a period in one response: the totals, their movement against
    /// the comparison range, the channels with their own figures, and the remarks a reader should
    /// see before them. The period is folded once along every dimension shown, so the blocks of the
    /// page cannot disagree about the same line. An omitted <c>completeness</c> means <c>all</c>:
    /// the overview is the full picture, and its totals are what the reader reconciles.
    /// </summary>
    [HttpGet("overview")]
    [ProducesResponseType(typeof(PnlOverviewResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<PnlOverviewResponse>> GetOverview(
        [FromQuery] Guid? channelId,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] DateOnly? compareFrom,
        [FromQuery] DateOnly? compareTo,
        [FromQuery] string? completeness,
        CancellationToken cancellationToken)
    {
        (DateOnly periodFrom, DateOnly periodTo) = PnlPeriodQuery.Require(from, to);

        ValidateCompleteness(completeness);

        PnlOverviewResponse response = await _mediator.Send(
            new GetPnlOverviewQuery
            {
                Filter = new PnlQueryFilter
                {
                    ChannelId = channelId,
                    From = periodFrom,
                    To = periodTo,
                    Completeness = string.IsNullOrWhiteSpace(completeness)
                        ? PnlCompletenessFilter.All
                        : completeness,
                },
                ComparisonFrom = compareFrom,
                ComparisonTo = compareTo,
            },
            cancellationToken);

        return Ok(response);
    }

    /// <summary>
    /// Returns the products ranked by one metric over the whole requested range. A ranking has no
    /// period or grouping axis: one row per product, <c>rank</c> assigned from 1, and the counts of
    /// the population the slice was taken from. A row without the ranked metric never enters the
    /// ranking, so <c>rankedRows</c> can be smaller than <c>totalRows</c>.
    /// </summary>
    [HttpGet("pnl-product-ranking")]
    [ProducesResponseType(typeof(PnlProductRankingResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<PnlProductRankingResponse>> GetProductRanking(
        [FromQuery] Guid? channelId,
        [FromQuery] Guid? channelProductId,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] string? completeness,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDir,
        [FromQuery] int? take,
        [FromQuery] string? coverage,
        [FromQuery] decimal? minRevenue,
        CancellationToken cancellationToken)
    {
        ValidateCompleteness(completeness);

        PnlRankingSortKey parsedSortBy = PnlRankingSortKey.Margin;

        if (sortBy != null
            && (!Enum.TryParse(sortBy, ignoreCase: true, out parsedSortBy)
                || !Enum.IsDefined(parsedSortBy)))
        {
            throw new ValidationException(
                new[]
                {
                    new ValidationFailure(
                        nameof(sortBy),
                        "Сортировка должна быть одной из: margin, roi, contributionMarginPercent, revenue, netRevenue, quantitySold, quantityOrdered."),
                });
        }

        bool descending = true;

        if (sortDir != null)
        {
            if (!string.Equals(sortDir, "asc", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase))
            {
                throw new ValidationException(
                    new[]
                    {
                        new ValidationFailure(nameof(sortDir), "Направление сортировки должно быть asc или desc."),
                    });
            }

            descending = !string.Equals(sortDir, "asc", StringComparison.OrdinalIgnoreCase);
        }

        PnlRankingCoverage parsedCoverage = PnlRankingCoverage.Complete;

        if (coverage != null
            && (!Enum.TryParse(coverage, ignoreCase: true, out parsedCoverage)
                || !Enum.IsDefined(parsedCoverage)))
        {
            throw new ValidationException(
                new[]
                {
                    new ValidationFailure(nameof(coverage), "Покрытие должно быть одним из: any, complete."),
                });
        }

        PnlProductRankingResponse response = await _mediator.Send(
            new GetPnlProductRankingQuery
            {
                Filter = new PnlQueryFilter
                {
                    ChannelId = channelId,
                    ChannelProductId = channelProductId,
                    From = from,
                    To = to,
                    Completeness = completeness,
                },
                SortBy = parsedSortBy,
                Descending = descending,
                Take = take ?? GetPnlProductRankingQuery.DefaultTake,
                Coverage = parsedCoverage,
                MinRevenue = minRevenue,
            },
            cancellationToken);

        return Ok(response);
    }

    /// <summary>
    /// Returns the period diagnostics: unattributed journal entries, ambiguous attributions and the
    /// aggregate control difference.
    /// </summary>
    [HttpGet("diagnostics")]
    [ProducesResponseType(typeof(PnlDiagnosticsResult), StatusCodes.Status200OK)]
    public async Task<ActionResult<PnlDiagnosticsResult>> GetDiagnostics(
        [FromQuery] Guid? channelId,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken cancellationToken)
    {
        (DateOnly periodFrom, DateOnly periodTo) = PnlPeriodQuery.Require(from, to);

        PnlDiagnosticsResult response = await _mediator.Send(
            new GetPnlDiagnosticsQuery { ChannelId = channelId, From = periodFrom, To = periodTo },
            cancellationToken);

        return Ok(response);
    }

    /// <summary>
    /// Returns the per-channel feed freshness: when each channel last delivered a batch and when its
    /// figures last changed. It takes no period filter on purpose вЂ” the age of the data is a property
    /// of the feed, not of the range the dashboard shows.
    /// </summary>
    [HttpGet("freshness")]
    [ProducesResponseType(typeof(PnlFeedFreshnessResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<PnlFeedFreshnessResponse>> GetFreshness(CancellationToken cancellationToken)
    {
        PnlFeedFreshnessResponse response = await _mediator.Send(
            new GetPnlFeedFreshnessQuery(),
            cancellationToken);

        return Ok(response);
    }

    private static void ValidateCompleteness(string? completeness)
    {
        if (PnlCompletenessFilter.TryParse(completeness, out _))
        {
            return;
        }

        throw new ValidationException(
            new[]
            {
                new ValidationFailure(
                    nameof(completeness),
                    $"Полнота должна быть одной из: {string.Join(", ", PnlCompletenessFilter.AcceptedValues)}."),
            });
    }
}
