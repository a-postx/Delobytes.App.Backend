# Многоэтапная сборка для .NET 8 приложения

# Этап 1: Сборка
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["Directory.Build.props", "./"]

# Копируем .csproj файлы для кеширования слоя восстановления зависимостей
COPY ["src/Delobytes.App.Backend/Delobytes.App.Backend.csproj", "src/Delobytes.App.Backend/"]
COPY ["src/Modules/Identity/Delobytes.App.Backend.Identity.Domain/Delobytes.App.Backend.Identity.Domain.csproj", "src/Modules/Identity/Delobytes.App.Backend.Identity.Domain/"]
COPY ["src/Modules/Identity/Delobytes.App.Backend.Identity.Application/Delobytes.App.Backend.Identity.Application.csproj", "src/Modules/Identity/Delobytes.App.Backend.Identity.Application/"]
COPY ["src/Modules/Identity/Delobytes.App.Backend.Identity.Infrastructure/Delobytes.App.Backend.Identity.Infrastructure.csproj", "src/Modules/Identity/Delobytes.App.Backend.Identity.Infrastructure/"]
COPY ["src/Modules/Catalog/Delobytes.App.Backend.Catalog.Domain/Delobytes.App.Backend.Catalog.Domain.csproj", "src/Modules/Catalog/Delobytes.App.Backend.Catalog.Domain/"]
COPY ["src/Modules/Catalog/Delobytes.App.Backend.Catalog.Application/Delobytes.App.Backend.Catalog.Application.csproj", "src/Modules/Catalog/Delobytes.App.Backend.Catalog.Application/"]
COPY ["src/Modules/Catalog/Delobytes.App.Backend.Catalog.Infrastructure/Delobytes.App.Backend.Catalog.Infrastructure.csproj", "src/Modules/Catalog/Delobytes.App.Backend.Catalog.Infrastructure/"]
COPY ["src/Modules/Pricing/Delobytes.App.Backend.Pricing.Domain/Delobytes.App.Backend.Pricing.Domain.csproj", "src/Modules/Pricing/Delobytes.App.Backend.Pricing.Domain/"]
COPY ["src/Modules/Pricing/Delobytes.App.Backend.Pricing.Application/Delobytes.App.Backend.Pricing.Application.csproj", "src/Modules/Pricing/Delobytes.App.Backend.Pricing.Application/"]
COPY ["src/Modules/Pricing/Delobytes.App.Backend.Pricing.Infrastructure/Delobytes.App.Backend.Pricing.Infrastructure.csproj", "src/Modules/Pricing/Delobytes.App.Backend.Pricing.Infrastructure/"]

# Восстанавливаем зависимости через главный проект
RUN dotnet restore "src/Delobytes.App.Backend/Delobytes.App.Backend.csproj"

# Копируем исходный код и общие свойства сборки
COPY ["src/", "src/"]

# Собираем главный проект в режиме Release
RUN dotnet build "src/Delobytes.App.Backend/Delobytes.App.Backend.csproj" -c Release --no-restore

# Публикуем приложение
RUN dotnet publish "src/Delobytes.App.Backend/Delobytes.App.Backend.csproj" -c Release --no-build -o /app/publish

# Этап 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Копируем опубликованное приложение из этапа сборки
COPY --from=build /app/publish .

# Настройка переменных окружения
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
ENV DOTNET_RUNNING_IN_CONTAINER=true
ENV DOTNET_EnableDiagnostics=0

# Открываем порт
EXPOSE 8080

# Точка входа
ENTRYPOINT ["dotnet", "Delobytes.App.Backend.dll"]
