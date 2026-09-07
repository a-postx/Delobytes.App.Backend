using System.Text.Json;
using Delobytes.App.Backend.Integrations.Application.DTOs;
using Delobytes.App.Backend.Integrations.Application.Events;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers;

/// <summary>
/// Consumer for processing sync jobs.
/// </summary>
public class ProcessSyncJobConsumer
{
    private readonly ISyncJobRepository _syncJobRepository;
    private readonly IRawApiResponseRepository _rawApiResponseRepository;
    private readonly IChannelApiClientFactory _clientFactory;
    private readonly ILogger<ProcessSyncJobConsumer> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProcessSyncJobConsumer"/> class.
    /// </summary>
    /// <param name="syncJobRepository">Sync job repository.</param>
    /// <param name="rawApiResponseRepository">Raw API response repository.</param>
    /// <param name="clientFactory">Channel API client factory.</param>
    /// <param name="logger">Logger instance.</param>
    public ProcessSyncJobConsumer(
        ISyncJobRepository syncJobRepository,
        IRawApiResponseRepository rawApiResponseRepository,
        IChannelApiClientFactory clientFactory,
        ILogger<ProcessSyncJobConsumer> logger)
    {
        _syncJobRepository = syncJobRepository;
        _rawApiResponseRepository = rawApiResponseRepository;
        _clientFactory = clientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Processes the StartSyncJobEvent message.
    /// </summary>
    /// <param name="message">Event message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ProcessAsync(StartSyncJobEvent message, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Processing StartSyncJobEvent for SyncJob {SyncJobId}", message.SyncJobId);

        SyncJob? syncJob = await _syncJobRepository.FindByIdWithConnectionAsync(message.SyncJobId, cancellationToken);

        if (syncJob == null)
        {
            _logger.LogWarning("SyncJob {SyncJobId} not found", message.SyncJobId);
            return;
        }

        Connection connection = syncJob.Connection;

        if (connection == null)
        {
            _logger.LogError("Connection {ConnectionId} not found for SyncJob {SyncJobId}", message.ConnectionId, message.SyncJobId);
            syncJob.Status = SyncJobStatus.Failed;
            syncJob.ErrorMessage = "Connection not found";
            syncJob.CompletedAt = DateTimeOffset.UtcNow;
            _syncJobRepository.Update(syncJob);
            await _syncJobRepository.SaveChangesAsync(cancellationToken);
            return;
        }

        syncJob.Status = SyncJobStatus.Running;
        syncJob.StartedAt = DateTimeOffset.UtcNow;
        _syncJobRepository.Update(syncJob);
        await _syncJobRepository.SaveChangesAsync(cancellationToken);

        try
        {
            IChannelApiClient apiClient = _clientFactory.Create(connection.Channel.Code, connection);

            ApiResponse<OrdersData> apiResponse = await apiClient.GetOrdersAsync(
                syncJob.DateRangeFrom,
                syncJob.DateRangeTo,
                cancellationToken);

            RawApiResponse rawResponse = new RawApiResponse
            {
                Id = Guid.NewGuid(),
                SyncJobId = syncJob.Id,
                Endpoint = "GetOrders",
                RequestPayload = JsonSerializer.Serialize(new { from = syncJob.DateRangeFrom, to = syncJob.DateRangeTo }),
                ResponsePayload = JsonSerializer.Serialize(apiResponse),
                HttpStatusCode = apiResponse.StatusCode ?? 0,
                ReceivedAt = apiResponse.Timestamp
            };

            _rawApiResponseRepository.Add(rawResponse);

            if (apiResponse.IsSuccess)
            {
                syncJob.Status = SyncJobStatus.Success;
                syncJob.RecordsProcessed = apiResponse.Data?.Orders?.Count ?? 0;
                _logger.LogInformation("SyncJob {SyncJobId} completed successfully with {RecordCount} records", syncJob.Id, syncJob.RecordsProcessed);
            }
            else
            {
                syncJob.Status = SyncJobStatus.Failed;
                syncJob.ErrorMessage = apiResponse.ErrorMessage ?? "Unknown error";
                _logger.LogWarning("SyncJob {SyncJobId} failed: {ErrorMessage}", syncJob.Id, syncJob.ErrorMessage);
            }

            syncJob.CompletedAt = DateTimeOffset.UtcNow;
            _syncJobRepository.Update(syncJob);
            await _syncJobRepository.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception occurred while processing SyncJob {SyncJobId}", message.SyncJobId);

            syncJob.Status = SyncJobStatus.Failed;
            syncJob.ErrorMessage = ex.Message;
            syncJob.CompletedAt = DateTimeOffset.UtcNow;
            _syncJobRepository.Update(syncJob);
            await _syncJobRepository.SaveChangesAsync(cancellationToken);
        }
    }
}
