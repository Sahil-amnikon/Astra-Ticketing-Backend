# .NET / React / SQL Server migration: developer review

This folder contains the incomplete native .NET migration. It is not the backend currently running the live portal. Do not use it as a completed application or switch the live application to it.

## Build on Windows

Install the .NET 10 SDK matching `global.json` (10.0.401 with latest-patch roll forward) and Node.js 24 with npm. Microsoft installation guidance: https://learn.microsoft.com/en-us/dotnet/core/install/windows.

Open PowerShell in `dotnet-migration` and run:

```powershell
.\windows\Build-DotNet-Windows.ps1
```

The solution contains `src/Astra.Api`, `tests/Astra.Tests` and `tools/Astra.Migrator`. The React frontend is in `apps/web`; SQL Server schema scripts are in `database`. Read `README.md`, `AGENTS.md`, `inventory/routes.json`, and the handoff VALIDATION.json before changing code.

## Run the candidate for development

The API can start with SQL Server/Redis settings supplied through process environment variables. The following values are examples to replace for your isolated local database; no live credentials are included:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = 'http://localhost:3001'
$env:APP_BASE_URL = 'http://localhost:3000'
$env:SQLSERVER_CONNECTION_STRING = 'Server=localhost;Database=AstraMigration;Integrated Security=True;Encrypt=True;TrustServerCertificate=True'
$env:REDIS_URL = 'redis://localhost:16379'
dotnet run --project src/Astra.Api -c Release --launch-profile http
```

In a second PowerShell terminal:

```powershell
$env:API_INTERNAL_URL = 'http://localhost:3001'
npm run dev --workspace @amnikon/web
```

Before starting the backend for an authenticated run, also set `ENTRA_TENANT_ID`,
`ENTRA_CLIENT_ID`, `ENTRA_CLIENT_SECRET` and `INITIAL_ADMIN_OBJECT_IDS` in that
PowerShell session. Register the Web redirect URI exactly as
`http://localhost:3000/api/auth/callback`. Keep the client secret out of source
files and chat. The HTTP launch profile binds the API to port 3001 so the Next.js
API proxy can reach it at `API_INTERNAL_URL`.

Administration's Entra user search also needs the Microsoft Graph **application**
permission `User.ReadBasic.All` with admin consent in the development tenant.
The backend uses the same client credentials for this server-to-server lookup.
Sign-in can work while directory search remains unavailable if that permission
has not been granted.

This only starts the candidate processes. Workspaces require a schema-compatible populated test database and configured Entra sign-in. Reference data is not seeded by the current .NET schema generator. Many routes, background jobs and administrative updates remain unported. No sign-in bypass is included. Do not run the candidate together with the current application on the same web port.

## Database review and bootstrap cautions

SQL Server 2025 Express was selected for the eventual live target. The approved isolated test currently uses Developer edition, database `AstraMigration`, on TKT-DB. The ZIP contains SQL source and schema metadata; it contains no copied records or database backup.

The migrator uses `SQLSERVER_TEST_CONNECTION_STRING` and restricts targets to `AstraMigration` or its named test variants. The current generated `001_schema.sql` already contains `client_mail_routes`; `006_client_mail_routes.sql` was a supplemental upgrade for the older test schema. Applying all numbered files blindly to a fresh database would duplicate that table. Script 005 preserves historical identity high-water marks for the existing trial and should not be treated as generic seed data. Reference tables and a final data transfer need deliberate setup by the developer. Read the migrator source and README rather than using the SQL folder as an unattended installer.

The source explicitly tracks missing endpoints in `inventory/routes.json`. The newly added TicketRead module compiles; its SQL-backed checks were interrupted by the previous network outage and must be rerun before treating those endpoints as verified. Last successful full SQL-backed validation covered 73 tests. The handoff does not claim every module works on SQL Server.
