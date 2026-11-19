using System.Text.Json;
using Delobytes.App.Backend.Contracts.Authorization;
using Delobytes.App.Backend.Integrations.Application.Finance;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Jobs;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Integrations.Application.Commands;

/// <summary>
/// Starts an operator-triggered finance re-import of an explicit date range. Uses the same
/// orchestration as a scheduled run; the explicit range is stored in the run's checkpoint and the
/// watermark is not touched by the request itself.
/// </summary>
public class StartFinanceImportCommand : IRequest<Guid>, IRequireRole
{
    /// <summary>Gets or sets the connection to import from.</summary>
    public Guid ConnectionId { get; set; }

    /// <summary>Gets or sets the finance job type.</summary>
    public JobType JobType { get; set; }

    /// <summary>Gets or sets the first date to fetch, channel-local.</summary>
    public DateOnly FromDate { get; set; }

    /// <summary>Gets or sets the last date to fetch, channel-local.</summary>
    public DateOnly ToDate { get; set; }

    /// <inheritdoc/>
    public Role[] AllowedRoles => new[] { Role.Administrator };
}

/// <summary>
/// Handles <see cref="StartFinanceImportCommand"/>.
/// </summary>
public class StartFinanceImportCommandHandler : IRequestHandler<StartFinanceImportCommand, Guid>
{
    private readonly JobRunStarter _jobRunStarter;
    private readonly FinanceImportCheckpointStore _checkpoints;
    private readonly ILogger<StartFinanceImportCommandHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="StartFinanceImportCommandHandler"/> class.
    /// </summary>
    public StartFinanceImportCommandHandler(
        JobRunStarter jobRunStarter,
        FinanceImportCheckpointStore checkpoints,
        ILogger<StartFinanceImportCommandHandler> logger)
    {
        _jobRunStarter = jobRunStarter;
        _checkpoints = checkpoints;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<Guid> Handle(StartFinanceImportCommand request, CancellationToken cancellationToken)
    {
        if (request.JobType is not (JobType.WbFinanceReportImport or JobType.OzonFinanceAccrualsImport))
        {
            throw new ArgumentOutOfRangeException(
                nameof(request.JobType), request.JobType, "The job type is not a finance import.");
        }

        if (request.FromDate > request.ToDate)
        {
            throw new ArgumentException("The range start must not be after its end.", nameof(request));
        }

        JobRun run = await _jobRunStarter.CreatePendingAsync(
            request.ConnectionId,
            request.JobType,
            requestedByUserId: null,
            cancellationToken);

        FinanceImportCheckpointPayload state = new()
        {
            ExplicitFrom = request.FromDate,
            ExplicitTo = request.ToDate,
        };

        await _checkpoints.SaveAsync(run.Id, state, cancellationToken);

        _logger.LogInformation(
            "Manual finance import requested. JobRunId={JobRunId}, JobType={JobType}, From={From}, To={To}",
            run.Id,
            request.JobType,
            request.FromDate,
            request.ToDate);

        return run.Id;
    }
}
