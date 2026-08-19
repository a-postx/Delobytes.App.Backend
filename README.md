# Delobytes App Backend

ASP.NET Core 7 backend for the Delobytes e-commerce margin accounting system.

## Architecture

Modular monolith with logical isolation of modules by domain boundaries.
Each module follows Clean Architecture layers: **Domain → Application → Infrastructure**.

```
Delobytes.App.Backend.sln
├── src/
│   ├── Delobytes.App.Backend/               # ASP.NET Core Web API host
│   └── Modules/
│       ├── Identity/
│       │   ├── Delobytes.App.Backend.Identity.Domain/
│       │   ├── Delobytes.App.Backend.Identity.Application/
│       │   └── Delobytes.App.Backend.Identity.Infrastructure/
│       ├── Catalog/
│       │   ├── Delobytes.App.Backend.Catalog.Domain/
│       │   ├── Delobytes.App.Backend.Catalog.Application/
│       │   └── Delobytes.App.Backend.Catalog.Infrastructure/
│       └── Pricing/
│           ├── Delobytes.App.Backend.Pricing.Domain/
│           ├── Delobytes.App.Backend.Pricing.Application/
│           └── Delobytes.App.Backend.Pricing.Infrastructure/
└── tests/
    └── Delobytes.App.Backend.Tests/
```

## Prerequisites

- [.NET 7 SDK](https://dotnet.microsoft.com/download/dotnet/7.0)
- PostgreSQL 14+ (local or Yandex Managed Service)

## Quick start (Ubuntu 20.04+)

```bash
# 1. Install .NET 7 SDK
wget https://dot.net/v1/dotnet-install.sh -O dotnet-install.sh
bash dotnet-install.sh --channel 7.0
export PATH="$PATH:$HOME/.dotnet"

# 2. Clone / unpack the project
cd Delobytes.App.Backend

# 3. Set the PostgreSQL connection string
export ConnectionStrings__DefaultConnection="Host=localhost;Database=delobytes_dev;Username=postgres;Password=<your-password>"
# Or edit src/Delobytes.App.Backend/appsettings.Development.json

# 4. Restore & run
dotnet restore
dotnet run --project src/Delobytes.App.Backend/Delobytes.App.Backend.csproj
```

The application starts on `http://localhost:5000` by default.

### Endpoints

| Path | Description |
|------|-------------|
| `/` | Swagger UI |
| `/swagger/v1/swagger.json` | OpenAPI spec |
| `/status` | Service liveness (JSON) |
| `/metrics` | Health checks / DB reachability (JSON) |

## Run unit tests

```bash
dotnet test tests/Delobytes.App.Backend.Tests/Delobytes.App.Backend.Tests.csproj --verbosity normal
```

## EF Core Migrations

Migrations are applied automatically on startup (`MigrationExtensions.ApplyMigrationsAsync`).
To add a new migration manually:

```bash
# Identity module
dotnet ef migrations add <MigrationName> \
  --project src/Modules/Identity/Delobytes.App.Backend.Identity.Infrastructure \
  --startup-project src/Delobytes.App.Backend \
  --context IdentityDbContext

# Catalog module
dotnet ef migrations add <MigrationName> \
  --project src/Modules/Catalog/Delobytes.App.Backend.Catalog.Infrastructure \
  --startup-project src/Delobytes.App.Backend \
  --context CatalogDbContext

# Pricing module
dotnet ef migrations add <MigrationName> \
  --project src/Modules/Pricing/Delobytes.App.Backend.Pricing.Infrastructure \
  --startup-project src/Delobytes.App.Backend \
  --context PricingDbContext
```

## Code style

StyleCop.Analyzers 1.1.x is applied to all projects via `Directory.Build.props`.
Code style rules are defined in `.editorconfig` (sourced from the reference repository).
Run `dotnet build` to see any style violations as warnings.

## CI

GitHub Actions workflow at `.github/workflows/ci.yml` — triggered on push/PR to `main`/`develop`.
Runs `dotnet build` (Release) + `dotnet test`.
