using FluentValidation;

namespace Delobytes.App.Backend.Integrations.Application.Commands.StartOrdersImport;

public class StartOrdersImportCommandValidator : AbstractValidator<StartOrdersImportCommand>
{
    public StartOrdersImportCommandValidator()
    {
        RuleFor(x => x.ConnectionId).NotEmpty();
    }
}
