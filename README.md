# ASTRA backend

Current ASTRA service: ASP.NET Core on .NET 10 with MySQL through MySqlConnector. This source replaces the earlier SQL Server migration candidate on main; previous versions remain in Git history.

The matching frontend is [Astra-Ticketing-Frontend](https://github.com/Sahil-amnikon/Astra-Ticketing-Frontend), using React, TypeScript and Vite.

## Build

Install the .NET 10 SDK:

```sh
dotnet restore Astra.Api.csproj
dotnet build Astra.Api.csproj
```

Runtime configuration belongs in protected host settings or environment variables. The MySQL connection uses `ConnectionStrings__MySql`. Configure authentication, reverse-proxy settings and integrations for the target environment; do not commit real configuration or credentials.

## Existing environment and deployment

This source is maintained against the existing ASTRA MySQL schema and deployed configuration. It is not a complete empty-database installer: the historical base schema and production settings are not included here. Provisioning a new environment requires the reviewed schema and configuration separately.

`local-maintenance/` contains application-aware verification, additive storage migrations, deployment helpers and labelled demo creators. Its scripts expect the existing service layout and sibling source folders named `ASTRA-Backend-latest` and `ASTRA-Frontend-latest`. Review the specific script before running it; many target the configured production VM.

**Pending deployment dependency:** the multiple-ticket-category code reads and writes `ticket_categories`. Its additive storage helper is `local-maintenance/migrate-ticket-categories.py`. Verify that the table exists before deploying this backend; do not assume an unrelated deployment helper creates it.

Runtime files under `App_Data/`, uploads, mail state, build output, deployment logs and private settings are excluded from Git. A source push does not deploy or migrate the application.

## Verification

The maintenance folder includes focused checks for endpoint binding, review authorization, change routing, article/media validation, reporting and ticket replies. Many are designed to run without a live application database. Read their launcher for build prerequisites. Compilation and fixture checks are not proof that a fresh database or production integrations work.

The project review identified remaining work around consistent ticket permission denial, comment write authorization, notification/database vocabulary, paused SLA priority changes and service-search pagination. These findings have not all been corrected by this source-publication task.
