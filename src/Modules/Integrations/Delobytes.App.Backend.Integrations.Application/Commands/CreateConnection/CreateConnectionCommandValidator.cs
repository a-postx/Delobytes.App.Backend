using FluentValidation;

namespace Delobytes.App.Backend.Integrations.Application.Commands.CreateConnection;

public class CreateConnectionCommandValidator : AbstractValidator<CreateConnectionCommand>
{
    public CreateConnectionCommandValidator()
    {
        // ChannelId приходит от клиента — канал должен быть создан заранее в модуле Catalog.
        RuleFor(x => x.ChannelId).NotEmpty();
        RuleFor(x => x.SystemChannelTemplateCode).NotEmpty();
        RuleFor(x => x.ApiKey).NotEmpty().MinimumLength(10);
    }
}
