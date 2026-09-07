using FluentValidation;

namespace Delobytes.App.Backend.Integrations.Application.Commands.CreateConnection;

public class CreateConnectionCommandValidator : AbstractValidator<CreateConnectionCommand>
{
    public CreateConnectionCommandValidator()
    {
        RuleFor(x => x.SystemChannelTemplateCode).NotEmpty();
        RuleFor(x => x.ApiKey).NotEmpty().MinimumLength(10);
    }
}
