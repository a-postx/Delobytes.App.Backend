using FluentValidation;

namespace Delobytes.App.Backend.Integrations.Application.Commands;

public class StartOrdersImportCommandValidator : AbstractValidator<StartOrdersImportCommand>
{
    public StartOrdersImportCommandValidator()
    {
        RuleFor(x => x.ConnectionId).NotEmpty();
    }
}
