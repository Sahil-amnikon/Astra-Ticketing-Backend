# ASTRA backend development

This is the current ASP.NET Core 10/MySQL application, paired with the React/Vite frontend. Earlier SQL Server migration instructions belong to historical commits.

- Read README.md, retain the current API contract, IDs, history and client/site isolation.
- Use application transactions and effective authorization checks for mutations.
- Keep credentials, production configuration, uploads, database contents, logs and generated output out of Git.
- Build `Astra.Api.csproj` and run focused maintenance checks appropriate to the change. Do not claim unexecuted production or database checks passed.
- Production schema changes are separate from publishing source. Verify required storage before deployment, including the pending ticket-category dependency.
- Do not deploy, modify production data, create backups or copy databases unless authorized by the user.
