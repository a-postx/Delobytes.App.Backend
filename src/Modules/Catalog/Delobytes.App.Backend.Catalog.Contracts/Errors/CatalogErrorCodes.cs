using Delobytes.App.Backend.Contracts.Errors;

namespace Delobytes.App.Backend.Catalog.Contracts.Errors;

/// <summary>
/// Machine-readable Catalog error codes. Owned by the module so that adding a Catalog failure
/// does not require editing the host.
/// </summary>
public static class CatalogErrorCodes
{
    public static readonly ErrorCode ProductNotFound = new ErrorCode("catalog.product.not_found", 404, "Товар не найден.");
    public static readonly ErrorCode ProductNotRestorable = new ErrorCode("catalog.product.not_restorable", 409, "Восстановить можно только товар в архиве или с неудачным удалением.");
    public static readonly ErrorCode ProductDeletionAlreadyInProgress = new ErrorCode("catalog.product.deletion_already_in_progress", 409, "Удаление товара уже выполняется.");
    public static readonly ErrorCode ProductAlreadyDeleted = new ErrorCode("catalog.product.already_deleted", 409, "Товар уже удалён.");
    public static readonly ErrorCode ProductWorkRateNotFound = new ErrorCode("catalog.product_work_rate.not_found", 404, "Норма выработки не найдена.");
    public static readonly ErrorCode WorkRateNotFound = new ErrorCode("catalog.work_rate.not_found", 404, "Ставка работы не найдена.");
    public static readonly ErrorCode ProductSkuConflict = new ErrorCode("catalog.product.sku_conflict", 409, "Товар с таким артикулом уже существует.");
    public static readonly ErrorCode ProductCodeAllocationFailed = new ErrorCode("catalog.product.code_allocation_failed", 409, "Не удалось выделить свободный код товара: полученные номера уже заняты.");
    public static readonly ErrorCode BomLineNotFound = new ErrorCode("catalog.bom_line.not_found", 404, "Строка BOM не найдена.");
    public static readonly ErrorCode BomLineInvalidQuantity = new ErrorCode("catalog.bom_line.invalid_quantity", 422, "Количество компонента должно быть больше нуля.");
    public static readonly ErrorCode BomComponentNotFound = new ErrorCode("catalog.bom_line.component_not_found", 404, "Компонент не найден.");
    public static readonly ErrorCode BomLineValidFromConflict = new ErrorCode("catalog.bom_line.valid_from_conflict", 409, "Дата начала действия должна быть позже даты начала действующей версии строки BOM.");
    public static readonly ErrorCode ProductBarcodeConflict = new ErrorCode("catalog.product_barcode.value_conflict", 409, "Этот штрихкод уже привязан к другому товару.");
    public static readonly ErrorCode ProductWorkRateValidFromConflict = new ErrorCode("catalog.product_work_rate.valid_from_conflict", 409, "Версия нормы с такой датой начала уже существует.");
    public static readonly ErrorCode ProductWorkRateValidFromNotLatest = new ErrorCode("catalog.product_work_rate.valid_from_not_latest", 409, "Дата начала действия должна быть позже даты предыдущей версии.");
    public static readonly ErrorCode NumberingExhausted = new ErrorCode("catalog.numbering.exhausted", 409, "Диапазон нумерации исчерпан.");
    public static readonly ErrorCode SupplierNotFound = new ErrorCode("catalog.supplier.not_found", 404, "Поставщик не найден.");
    public static readonly ErrorCode ComponentNotFound = new ErrorCode("catalog.component.not_found", 404, "Компонент не найден.");
    public static readonly ErrorCode ComponentUnitImmutable = new ErrorCode("catalog.component.unit_immutable", 409, "Единицу измерения нельзя изменить: от неё зависят сохранённые цены компонента и количества в составе товаров.");
    public static readonly ErrorCode ChannelNotFound = new ErrorCode("catalog.channel.not_found", 404, "Канал продаж не найден.");
}
