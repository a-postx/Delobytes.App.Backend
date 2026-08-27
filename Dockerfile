# Многоэтапная сборка для .NET 8 приложения
# Этап 1: Сборка
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Копируем файлы решения и конфигурации проекта
COPY ["Delobytes.App.Backend.sln", "./"]
COPY ["Directory.Build.props", "./"]

# Копируем все .csproj файлы для восстановления зависимостей
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
COPY ["tests/Delobytes.App.Backend.Tests/Delobytes.App.Backend.Tests.csproj", "tests/Delobytes.App.Backend.Tests/"]

# Восстанавливаем зависимости
RUN dotnet restore "Delobytes.App.Backend.sln"

# Копируем весь исходный код
COPY . .

# Собираем проект в режиме Release
WORKDIR "/src/src/Delobytes.App.Backend"
RUN dotnet build "Delobytes.App.Backend.csproj" -c Release -o /app/build --no-restore

# Этап 2: Публикация
FROM build AS publish
RUN dotnet publish "Delobytes.App.Backend.csproj" -c Release -o /app/publish --no-restore --no-build /p:UseAppHost=false

# Этап 3: Финальный образ
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# Копируем опубликованное приложение
COPY --from=publish /app/publish .

# Настройка переменных окружения
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
ENV DOTNET_RUNNING_IN_CONTAINER=true
ENV DOTNET_EnableDiagnostics=0

# Открываем порт
EXPOSE 8080

# Точка входа
ENTRYPOINT ["dotnet", "Delobytes.App.Backend.dll"]
