using FluentValidation;

namespace Delobytes.App.Backend.Catalog.Application.Commands.Components;

/// <summary>
/// Appending a price version had no validation: a negative price or a zero quantity produced a
/// version that every later cost calculation silently prices at zero.
/// </summary>
public class CreateComponentPriceCommandValidator : AbstractValidator<CreateComponentPriceCommand>
{
    public CreateComponentPriceCommandValidator()
    {
        RuleFor(x => x.ComponentId)
            .NotEmpty();

        RuleFor(x => x.PricePerUnit)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Цена не может быть отрицательной.");

        RuleFor(x => x.Quantity)
            .GreaterThan(0)
            .WithMessage("Количество должно быть больше нуля.");
    }
}
