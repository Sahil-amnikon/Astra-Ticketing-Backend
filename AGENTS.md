# ASTRA .NET and SQL Server migration

The user explicitly selected migration to .NET + React + Microsoft SQL Server on 13 October 2026. This supersedes the previous release's NestJS/PostgreSQL architecture requirement for this migration only.

Keep the current React/Next.js frontend contract. Port the backend natively to ASP.NET Core; a proxy to the old Node backend is not a completed migration. Preserve IDs, historic ticket numbers, client/site isolation, capped workspace roles, read-only Administration, audit history, attachments and encrypted mail settings. Track route parity in inventory/routes.json.

The live baseline is releases/astra-admin-service-config-20261013. Develop and test side by side. Do not replace the live application or restore its database until every module has passed parity checks and data-transfer reconciliation. Save an application rollback and a protected database backup before final cutover. Report pending work honestly.
