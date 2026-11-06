using Delobytes.App.Backend.Catalog.Domain.Enums;
using FluentValidation;

namespace Delobytes.App.Backend.Catalog.Application.Commands.Components;

/// <summary>
/// Category = Unknown (CLR default 0) is the sentinel EF Core relies on to decide whether to
/// apply the database default; it must never reach the handler as a real value, so a missing or
/// garbage category in the request is rejected here rather than silently stored as Unknown.
/// </summary>
public class UpdateComponentCommandValidator : AbstractValidator<UpdateComponentCommand>
{
    public UpdateComponentCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty();

        RuleFor(x => x.Name)
            .NotEmpty()
            .WithMessage("Название не может быть пустым.");

        RuleFor(x => x.Category)
            .IsInEnum()
            .WithMessage("Недопустимая категория компонента.")
            .NotEqual(ComponentCategory.Unknown)
            .WithMessage("Укажите категорию компонента.");
    }
}
