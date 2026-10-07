using FluentValidation;

namespace Delobytes.App.Backend.Catalog.Application.Commands.ProductWorkRates.CreateProductWorkRate;

/// <summary>
/// Field-level validation only, no database access. ValidFrom ordering against other versions of
/// the same product, and duplicate-date rejection, depend on data already in the database and are
/// therefore enforced in CreateProductWorkRateCommandHandler, next to UniqueConstraintTranslator;
/// see the class comment on UpdateProductCommandValidator for the rationale behind that split.
/// </summary>
public class CreateProductWorkRateCommandValidator : AbstractValidator<CreateProductWorkRateCommand>
{
    public CreateProductWorkRateCommandValidator()
    {
        RuleFor(x => x.ProductId)
            .NotEmpty();

        RuleFor(x => x.WorkRateId)
            .NotEmpty();

        RuleFor(x => x.AssemblyRatePerDay)
            .GreaterThan(0)
            .WithMessage("Норма выработки в день должна быть больше нуля.");

        RuleFor(x => x.ValidFrom)
            .NotEqual(default(DateOnly))
            .WithMessage("Укажите дату начала действия нормы.");
    }
}
