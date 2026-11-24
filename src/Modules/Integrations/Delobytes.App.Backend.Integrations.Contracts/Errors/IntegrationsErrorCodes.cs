using Delobytes.App.Backend.Contracts.Errors;

namespace Delobytes.App.Backend.Integrations.Contracts.Errors;

/// <summary>
/// Machine-readable Integrations error codes. Owned by the module so that adding an Integrations
/// failure does not require editing the host.
/// </summary>
public static class IntegrationsErrorCodes
{
    public static readonly ErrorCode ConnectionNotFound = new ErrorCode("integrations.connection.not_found", 404, "Подключение не найдено.");
    public static readonly ErrorCode ChannelTemplateNotFound = new ErrorCode("integrations.channel_template.not_found", 404, "Канал не найден.");
    public static readonly ErrorCode JobNotFound = new ErrorCode("integrations.job.not_found", 404, "Задача не найдена.");
    public static readonly ErrorCode JobAlreadyRunning = new ErrorCode("integrations.job.already_running", 409, "Для этого подключения уже есть активная задача импорта.");
    public static readonly ErrorCode ConnectionAlreadyActive = new ErrorCode("integrations.connection.already_active", 409, "К этому каналу продаж уже привязано активное подключение.");
    public static readonly ErrorCode ConnectionInactive = new ErrorCode("integrations.connection.inactive", 409, "Подключение неактивно: импорт невозможен.");
    public static readonly ErrorCode ChannelJobTypeAmbiguous = new ErrorCode("integrations.channel.job_type_ambiguous", 409, "Канал объявляет несколько типов задач импорта товаров.");
    public static readonly ErrorCode UnknownJobType = new ErrorCode("integrations.job.unknown_type", 400, "Неизвестный тип задачи.");
    public static readonly ErrorCode JobNotManuallyStartable = new ErrorCode("integrations.job.not_manually_startable", 409, "Эта задача выполняется планировщиком по расписанию и не запускается вручную.");
    public static readonly ErrorCode JobTypeNotSupported = new ErrorCode("integrations.job.type_not_supported", 422, "Запуск задач этого типа через API не поддерживается.");
    public static readonly ErrorCode JobNotCancellable = new ErrorCode("integrations.job.not_cancellable", 409, "Задача этого типа не поддерживает отмену.");
    public static readonly ErrorCode JobNotInCancellableState = new ErrorCode("integrations.job.not_cancellable_state", 409, "Отмена доступна только для задач со статусом Pending или Running.");
    public static readonly ErrorCode ProductsImportNotSupported = new ErrorCode("integrations.job.products_import_not_supported", 409, "Импорт карточек товаров не поддерживается для этого канала.");
    public static readonly ErrorCode ApiKeyValidationFailed = new ErrorCode("integrations.connection.api_key_invalid", 422, "Не удалось проверить ключи подключения.");
}
