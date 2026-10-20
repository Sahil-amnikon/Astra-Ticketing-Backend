# ASTRA native backend migration

Live SQL Server edition selected by the user: SQL Server Express. `infra/compose.sqlserver.express.yml` is prepared; the current isolated instance remains Developer until the native release and backups are ready.

Target: ASP.NET Core (.NET 10), the existing React/Next.js interface, and Microsoft SQL Server 2025. This is an incomplete migration candidate, not the live application.

The live baseline is `releases/astra-admin-service-config-20261013`, using NestJS and PostgreSQL. It has not been switched. The private SQL Server Developer instance on TKT-DB is an isolated migration test; its listener is loopback-only and accessed through an SSH tunnel.

## Verified so far

- The user approved a full-data trial into `AstraMigration` on TKT-DB. All 69 application tables reconciled using column-aware SHA-256 record hashes. The source transaction was repeatable-read and read-only. No application records were exported to local files.
- The .NET solution builds without warnings or errors. The React production build and TypeScript check pass.
- 73 tests pass with the isolated SQL Server connection supplied; none are skipped. They cover database parameters, workspace capping, read-only Administration, CSRF, session revocation, a mock-provider OIDC code flow with PKCE, invalid identity tokens, service reports, Administration lists, group permissions, domain configuration, and concurrent site ticket numbering.
- The full schema has 69 tables, 550 captured columns, 79 translated validation checks and 145 validated foreign keys. Forty-six ordinary/expression indexes were ported. Six text columns were widened to match their source capacity. Historic identity high-water marks, change-history immutability and prefix reservation safeguards are installed in the test database.
- The React API proxy preserves separate authentication cookies. Existing React pages are retained.

The original application account could not read `client_mail_routes`; an owner-account read-only inventory identified this additional table. It was added and reconciled in a supplemental transaction. The corrected full inventory covers 69 application tables and excludes only the two Knex migration-control tables. The copied application record count is 1,212. A fresh all-table comparison found only the live `mail_sync_state` polling timestamp had changed; all other 68 tables still matched exactly. This expected live drift is recorded honestly in `artifacts/data-verification-result.json`, whose current-snapshot `passed` flag is false.

See `artifacts/data-transfer-result.json` and `artifacts/test-results/sla-configuration-validation.trx` for evidence. The report records the successful trial at the time of copying. Subsequent tests create temporary dummy fixtures only in the isolated database and clean them up. Identity/sequence values can advance during tests.

## Native routes

Implemented and tested: authentication (`status`, `login`, `callback`, `me`, `logout`); service reports; Administration overview and resource lists; customer/staff groups; client-domain configuration. Liveness and dependency readiness are implemented. Core catalog lists and creation are also implemented and tested. Notifications and SLA calendar configuration are also implemented and tested. SLA policy update is implemented with valid-policy/application checks still pending. Native business-calendar calculation handles working days, holidays, partial minutes and daylight saving. Microsoft Entra tests use a local fake provider; an actual Entra/browser sign-in against the candidate is still required. Redis session storage is implemented, but current HTTP tests substitute an in-memory store; real Redis integration remains pending.

Track every existing endpoint in `inventory/routes.json`. Unported endpoints remain pending. A Node backend proxy does not constitute completion.

## Remaining cutover requirements

1. Port and validate native ticket, change and problem workflows, including client/support portals and their authorization rules.
2. Port administrative updates, identity provisioning, core catalog data, SLA/business calendars, knowledge/help, dashboards/report exports, notifications, mail configuration, inbound/outbound processing and workers.
3. Review 138 foreign-key delete/update actions. Current SQL foreign keys intentionally use NO ACTION until native deletion behavior is proven. Replace nine PostgreSQL search indexes and validate search semantics and performance.
4. Complete secure least-privilege SQL roles, secret/configuration mapping, real Redis tests, background job parity, packaging and full browser checks.
5. Freeze writes for an approved final cutover; capture fresh sequence state; transfer and reconcile a fresh final snapshot; preserve a protected PostgreSQL backup and application rollback; then switch and verify every workspace. The current trial is not a final cutover copy.

## Development

Use the SDK pinned in `global.json`. Supply connection strings through environment variables or protected host configuration. Do not put credentials in source files or logs.

```text
dotnet build Astra.sln -c Release
dotnet test Astra.sln -c Release
npm ci
npm run build:web
```

Without `SQLSERVER_TEST_CONNECTION_STRING`, SQL integration tests explicitly skip. Setting it to the isolated `AstraMigration` database enables integration checks. The API uses `SQLSERVER_CONNECTION_STRING` (or `ConnectionStrings:Astra`), `REDIS_URL`, `APP_BASE_URL`, and the existing Entra settings. Production sign-in also requires a protected persistent `DATA_PROTECTION_KEY_DIRECTORY`.

The migrator restricts target names to `AstraMigration` or explicitly named variants. `copy` refuses a target with existing application records. `apply` records and verifies migration checksums; schema upgrades run in a transaction. Copying sensitive live records to a different destination requires user authorization.
