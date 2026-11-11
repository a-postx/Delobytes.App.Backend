using Delobytes.App.Backend.Contracts.Accounting;
using FluentValidation;

namespace Delobytes.App.Backend.Identity.Application.Commands;

/// <summary>
/// Валидатор <see cref="UpdateTenantSettingsCommand"/>.
/// </summary>
public class UpdateTenantSettingsCommandValidator : AbstractValidator<UpdateTenantSettingsCommand>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateTenantSettingsCommandValidator"/> class.
    /// </summary>
    public UpdateTenantSettingsCommandValidator()
    {
        RuleFor(x => x.TenantId)
            .NotEmpty();

        RuleFor(x => x.TimeZone)
            .NotEmpty()
            .MaximumLength(64)
            // Проверяется то, что реально умеет сервер: Windows-идентификаторы на Linux
            // не разрешаются, поэтому принимается ровно тот формат, который вернёт клиенту
            // рабочий пояс, а не будет принят и упавшим при первом чтении.
            .Must(TenantTimeZones.IsValid)
            .WithMessage("Часовой пояс не распознан.");
    }
}
