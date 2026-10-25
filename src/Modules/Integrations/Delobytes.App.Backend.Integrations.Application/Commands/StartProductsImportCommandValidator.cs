using FluentValidation;

namespace Delobytes.App.Backend.Integrations.Application.Commands;

public class StartProductsImportCommandValidator : AbstractValidator<StartProductsImportCommand>
{
    public StartProductsImportCommandValidator()
    {
        RuleFor(x => x.ConnectionId).NotEmpty();
    }
}
