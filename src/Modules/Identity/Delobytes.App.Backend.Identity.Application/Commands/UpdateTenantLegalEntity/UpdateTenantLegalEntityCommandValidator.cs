using FluentValidation;

namespace Delobytes.App.Backend.Identity.Application.Commands.UpdateTenantLegalEntity;

/// <summary>
/// Validator for UpdateTenantLegalEntityCommand.
/// </summary>
public class UpdateTenantLegalEntityCommandValidator : AbstractValidator<UpdateTenantLegalEntityCommand>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateTenantLegalEntityCommandValidator"/> class.
    /// </summary>
    public UpdateTenantLegalEntityCommandValidator()
    {
        RuleFor(x => x.TenantId)
            .NotEmpty();

        RuleFor(x => x.LegalName)
            .MaximumLength(500)
            .When(x => x.LegalName != null);

        RuleFor(x => x.Inn)
            .MaximumLength(12)
            .When(x => x.Inn != null);
    }
}
