namespace Delobytes.App.Backend.Contracts.Errors;

/// <summary>
/// Error codes shared by the host and every module. Domain codes are declared by the owning
/// module next to its code, not here.
/// </summary>
public static class ErrorCodes
{
    public static class Common
    {
        public static readonly ErrorCode Unauthorized = new ErrorCode("common.unauthorized", 401, "Требуется аутентификация.");
        public static readonly ErrorCode Forbidden = new ErrorCode("common.forbidden", 403, "Недостаточно прав.");
        public static readonly ErrorCode NotFound = new ErrorCode("common.not_found", 404, "Ресурс не найден.");
        public static readonly ErrorCode RouteNotFound = new ErrorCode("common.route_not_found", 404, "Запрошенный адрес не найден.");
        public static readonly ErrorCode Conflict = new ErrorCode("common.conflict", 409, "Конфликт при выполнении операции.");
        public static readonly ErrorCode ValidationFailed = new ErrorCode("common.validation_failed", 422, "Проверьте корректность отправленных данных.");
        public static readonly ErrorCode Unexpected = new ErrorCode("common.unexpected_error", 500, "An unexpected error occurred.");
    }
}
