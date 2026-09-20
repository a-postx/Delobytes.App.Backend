namespace Delobytes.App.Backend.Contracts.Errors;

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

    public static class Catalog
    {
        public static readonly ErrorCode ProductWorkRateNotFound = new ErrorCode("catalog.product_work_rate.not_found", 404, "Норма выработки не найдена.");
    }
}
