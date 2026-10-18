using Delobytes.App.Backend.Integrations.Application.Extensions;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using MediatR;

namespace Delobytes.App.Backend.Integrations.Application.Commands.CancelJob;

public class CancelJobCommandHandler : IRequestHandler<CancelJobCommand>
{
    private readonly IJobRunRepository _jobRunRepository;

    public CancelJobCommandHandler(IJobRunRepository jobRunRepository)
    {
        _jobRunRepository = jobRunRepository;
    }

    public async Task Handle(CancelJobCommand request, CancellationToken cancellationToken)
    {
        JobRun? jobRun = await _jobRunRepository.GetByIdAsync(request.JobRunId, cancellationToken);

        if (jobRun == null)
        {
            throw new KeyNotFoundException($"Задача с ID '{request.JobRunId}' не найдена.");
        }

        if (!jobRun.JobType.IsCancellable())
        {
            throw new InvalidOperationException(
                $"Задача типа '{jobRun.JobType.ToStringCamelCase()}' не поддерживает отмену: " +
                "она выполняется по расписанию, и прерывание ломает непрерывность watermark.");
        }

        if (jobRun.Status != JobStatus.Pending && jobRun.Status != JobStatus.Running)
        {
            throw new InvalidOperationException(
                $"Задача в статусе '{jobRun.Status}' не может быть отменена. " +
                "Отмена доступна только для задач со статусом Pending или Running.");
        }

        // CompletedAt и счётчики здесь не финализируются: уже опубликованные батчи доигрывают
        // до конца, итоговую статистику записывает агрегатор после получения статуса Cancelled.
        jobRun.Status = JobStatus.Cancelled;

        await _jobRunRepository.UpdateAsync(jobRun, cancellationToken);
    }
}
