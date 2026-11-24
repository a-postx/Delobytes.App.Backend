# Многоэтапная сборка для .NET 10 приложения

# Этап 1: Сборка
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Список проектов здесь не дублируется: добавление модуля не может сломать образ.
# Кеш пакетов NuGet переиспользуется между сборками, поэтому отдельный слой restore не нужен.
COPY ["Directory.Build.props", "./"]
COPY ["src/", "src/"]

RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet publish "src/Delobytes.App.Backend/Delobytes.App.Backend.csproj" -c Release -o /app/publish

# Этап 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Копируем опубликованное приложение из этапа сборки
COPY --from=build /app/publish .

# Настройка переменных окружения
ENV ASPNETCORE_ENVIRONMENT=Production
ENV DOTNET_RUNNING_IN_CONTAINER=true
ENV DOTNET_EnableDiagnostics=0

# Открываем порт
EXPOSE 8080

# Точка входа
ENTRYPOINT ["dotnet", "Delobytes.App.Backend.dll"]
