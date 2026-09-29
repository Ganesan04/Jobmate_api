# Jobmate API

ASP.NET Core 9 + PostgreSQL backend for the Jobmate Flutter app.

## Layout

```
src/
  Jobmate.Domain          entities, enums, business rules, value objects (no dependencies)
  Jobmate.Application     MediatR commands/queries, validators, DTOs — grouped by feature
    DTOs/<Feature>/         <Feature>Dtos.cs (responses) + <Feature>Requests.cs (request bodies)
    Features/<Feature>/     Commands/ Queries/ Validators/
  Jobmate.Infrastructure  EF Core, migrations, JWT, BCrypt, seeders, mock OTP/payment/push
  Jobmate.Api             controllers, middleware, local media storage, Program.cs
tests/                    xUnit: API acceptance (real Postgres), domain rules, infrastructure
scripts/smoke-test.ps1    end-to-end check against a running API
```

## Setup

Secrets live in user-secrets, not appsettings:

```powershell
cd src/Jobmate.Api
dotnet user-secrets set "ConnectionStrings:WriteConnection" "Host=localhost;Port=5432;Database=jobmate;Username=jobmate;Password=..."
dotnet user-secrets set "ConnectionStrings:ReadConnection" "Host=localhost;Port=5432;Database=jobmate;Username=jobmate;Password=..."
dotnet user-secrets set "Jwt:Issuer" "jobmate-api"
dotnet user-secrets set "Jwt:Audience" "jobmate-app"
dotnet user-secrets set "Jwt:SigningKey" "<at least 32 random bytes>"
dotnet user-secrets set "Seed:AdminPassword" "<initial password for admin@jobmate.app>"
```

Apply migrations and run:

```powershell
dotnet ef database update --project src/Jobmate.Infrastructure --startup-project src/Jobmate.Api
dotnet run --project src/Jobmate.Api          # http://localhost:5234, Swagger at /swagger
```

## Tests

API tests run against a separate `jobmate_test` database: `WriteConnection` above with the
database name swapped (or `JOBMATE_TEST_CONNECTION`). They truncate it between tests.

```powershell
dotnet test
```
