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
        public static readonly ErrorCode WorkRateNotFound = new ErrorCode("catalog.work_rate.not_found", 404, "Ставка работы не найдена.");
        public static readonly ErrorCode ProductSkuConflict = new ErrorCode("catalog.product.sku_conflict", 409, "Товар с таким артикулом уже существует.");
        public static readonly ErrorCode BomLineNotFound = new ErrorCode("catalog.bom_line.not_found", 404, "Строка BOM не найдена.");
        public static readonly ErrorCode BomLineInvalidQuantity = new ErrorCode("catalog.bom_line.invalid_quantity", 422, "Количество компонента должно быть больше нуля.");
        public static readonly ErrorCode BomComponentNotFound = new ErrorCode("catalog.bom_line.component_not_found", 404, "Компонент не найден.");
        public static readonly ErrorCode ProductBarcodeConflict = new ErrorCode("catalog.product_barcode.value_conflict", 409, "Этот штрихкод уже привязан к другому товару.");
    }

    public static class Identity
    {
        public static readonly ErrorCode TenantTaxProfileNotFound = new ErrorCode("identity.tenant_tax_profile.not_found", 404, "Налоговый профиль не найден.");
        public static readonly ErrorCode TenantTaxProfileConflict = new ErrorCode("identity.tenant_tax_profile.conflict", 409, "Версия налогового профиля с такой датой начала уже существует.");
        public static readonly ErrorCode TenantTaxProfileNotLatest = new ErrorCode("identity.tenant_tax_profile.not_latest", 409, "Удалить можно только последнюю версию налогового профиля.");
        public static readonly ErrorCode TenantTaxProfileAlreadyEffective = new ErrorCode("identity.tenant_tax_profile.already_effective", 409, "Нельзя удалить ставку, которая уже вступила в силу.");
    }
}
