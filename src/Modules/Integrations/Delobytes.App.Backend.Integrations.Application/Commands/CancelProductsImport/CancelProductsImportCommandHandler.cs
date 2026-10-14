using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using MediatR;

namespace Delobytes.App.Backend.Integrations.Application.Commands.CancelProductsImport;

public class CancelProductsImportCommandHandler : IRequestHandler<CancelProductsImportCommand>
{
    private readonly IJobRunRepository _jobRunRepository;

    public CancelProductsImportCommandHandler(IJobRunRepository jobRunRepository)
    {
        _jobRunRepository = jobRunRepository;
    }

    public async Task Handle(CancelProductsImportCommand request, CancellationToken cancellationToken)
    {
        JobRun? jobRun = await _jobRunRepository.GetByIdAsync(request.JobRunId, cancellationToken);

        if (jobRun == null)
        {
            throw new KeyNotFoundException($"Задача импорта с ID '{request.JobRunId}' не найдена.");
        }

        if (jobRun.JobType != JobType.ProductsImport)
        {
            throw new InvalidOperationException(
                $"Отмена поддерживается только для задач типа ProductsImport. Текущий тип: '{jobRun.JobType}'.");
        }

        if (jobRun.Status != JobStatus.Pending && jobRun.Status != JobStatus.Running)
        {
            throw new InvalidOperationException(
                $"Задача в статусе '{jobRun.Status}' не может быть отменена. " +
                "Отмена доступна только для задач со статусом Pending или Running.");
        }

        // CompletedAt и счётчики не выставляются здесь: пока не завершены уже опубликованные
        // батчи, итоговую статистику зафиксирует агрегатор после отмены.
        jobRun.Status = JobStatus.Cancelled;

        await _jobRunRepository.UpdateAsync(jobRun, cancellationToken);
    }
}
