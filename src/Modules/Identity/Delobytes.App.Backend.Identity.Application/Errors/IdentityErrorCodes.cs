using Delobytes.App.Backend.Contracts.Errors;

namespace Delobytes.App.Backend.Identity.Application.Errors;

/// <summary>
/// Machine-readable Identity error codes. Owned by the module so that adding an Identity failure
/// does not require editing the host.
/// </summary>
public static class IdentityErrorCodes
{
    public static readonly ErrorCode TenantNotFound = new ErrorCode("identity.tenant.not_found", 404, "Аккаунт не найден.");
    public static readonly ErrorCode UserNotFound = new ErrorCode("identity.user.not_found", 404, "Пользователь не найден.");
    public static readonly ErrorCode InvitationNotFound = new ErrorCode("identity.invitation.not_found", 404, "Приглашение не найдено.");
    public static readonly ErrorCode MembershipNotFound = new ErrorCode("identity.membership.not_found", 404, "Участник аккаунта не найден.");
    public static readonly ErrorCode AccountDisabled = new ErrorCode("identity.user.disabled", 403, "Учётная запись отключена. Обратитесь к администратору.");
    public static readonly ErrorCode EmailAlreadyExists = new ErrorCode("identity.user.email_conflict", 409, "Аккаунт с таким email уже существует.");
    public static readonly ErrorCode PasswordNotSet = new ErrorCode("identity.user.password_not_set", 422, "Для этого аккаунта пароль не задан.");
    public static readonly ErrorCode CurrentPasswordIncorrect = new ErrorCode("identity.user.password_incorrect", 422, "Текущий пароль неверен.");
    public static readonly ErrorCode InvitationAlreadyAccepted = new ErrorCode("identity.invitation.already_accepted", 409, "Приглашение уже принято.");
    public static readonly ErrorCode InvitationExpired = new ErrorCode("identity.invitation.expired", 409, "Срок действия приглашения истёк.");
    public static readonly ErrorCode InvitationEmailMismatch = new ErrorCode("identity.invitation.email_mismatch", 403, "Приглашение предназначено для другого email адреса.");
    public static readonly ErrorCode InvitationAlreadyExists = new ErrorCode("identity.invitation.already_exists", 409, "Активное приглашение для этого email уже существует.");
    public static readonly ErrorCode AlreadyMember = new ErrorCode("identity.membership.already_member", 409, "Пользователь уже является членом этого аккаунта.");
    public static readonly ErrorCode LastAdministratorProtected = new ErrorCode("identity.membership.last_administrator", 409, "Нельзя оставить аккаунт без администратора.");
    public static readonly ErrorCode TenantLimitReached = new ErrorCode("identity.tenant.limit_reached", 409, "Достигнут максимальный лимит аккаунтов.");
    public static readonly ErrorCode CurrentTenantRequired = new ErrorCode("identity.tenant.current_required", 422, "Для создания дополнительного аккаунта необходимо указать текущий активный аккаунт.");
    public static readonly ErrorCode TenantTaxProfileNotFound = new ErrorCode("identity.tenant_tax_profile.not_found", 404, "Налоговый профиль не найден.");
    public static readonly ErrorCode TenantTaxProfileConflict = new ErrorCode("identity.tenant_tax_profile.conflict", 409, "Версия налогового профиля с такой датой начала уже существует.");
    public static readonly ErrorCode TenantTaxProfileNotLatest = new ErrorCode("identity.tenant_tax_profile.not_latest", 409, "Удалить можно только последнюю версию налогового профиля.");
    public static readonly ErrorCode TenantTaxProfileAlreadyEffective = new ErrorCode("identity.tenant_tax_profile.already_effective", 409, "Нельзя удалить ставку, которая уже вступила в силу.");
}
