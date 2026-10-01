using FluentValidation;

namespace Delobytes.App.Backend.Identity.Application.Commands.TaxProfiles.CreateTenantTaxProfile;

/// <summary>
/// Validator for CreateTenantTaxProfileCommand.
/// </summary>
public class CreateTenantTaxProfileCommandValidator : AbstractValidator<CreateTenantTaxProfileCommand>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CreateTenantTaxProfileCommandValidator"/> class.
    /// </summary>
    public CreateTenantTaxProfileCommandValidator()
    {
        RuleFor(x => x.TenantId)
            .NotEmpty();

        RuleFor(x => x.Regime)
            .IsInEnum()
            .WithMessage("Недопустимое значение налогового режима.");

        RuleFor(x => x.RatePercent)
            .InclusiveBetween(0m, 100m)
            .WithMessage("Ставка налога должна быть от 0 до 100 процентов.");

        RuleFor(x => x.Vat)
            .IsInEnum()
            .WithMessage("Недопустимое значение режима НДС.");
    }
}
