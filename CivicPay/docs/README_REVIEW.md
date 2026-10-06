# Public README review and setup verification

Reviewed October 6, 2026 (America/Edmonton). This record covers the repository-root README and `CivicPay/README.md`.

## Claims checked against implementation

| README claim group                                  | Source / review outcome                                                                                                                                                                                                                                                            |
| --------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Fictional municipal platform, shared-database scope | [Domain entities](../src/CivicPay.Domain/Entities.cs), [API host](../src/CivicPay.Api/Program.cs). Municipality-scoped records; shared API key, no tenant-specific permissions. No real payment provider.                                                                          |
| Configurable rules and supported catalogue          | [Contracts](../src/CivicPay.Application/Contracts.cs), [rules](../src/CivicPay.Application/Rules.cs), [configuration service](../src/CivicPay.Infrastructure/ConfigurationService.cs). CAD only; minimum/partial policy applies to the municipality.                               |
| Replay, conflicts, balances and concurrent requests | [Payment service](../src/CivicPay.Infrastructure/PaymentService.cs), [database mappings](../src/CivicPay.Infrastructure/CivicPayDbContext.cs), existing API/regression tests. No outbound queue or callback service claimed.                                                       |
| CSV-to-SQL migration and reconciliation             | [Importer](../src/CivicPay.Infrastructure/ImportService.cs), [CSV parser](../src/CivicPay.Application/CsvReader.cs), [SQL artifacts](../sql/). Application-managed imports; no direct legacy connector or ETL engine. Row-level commits and incomplete source totals are explicit. |
| Dashboard and request metrics                       | [Dashboard](../dashboard/app.js), [monitoring endpoint](../src/CivicPay.Api/Controllers/MonitoringController.cs), [UI verification](UI_UX_VERIFICATION.md). Bounded views; API request rate distinguished from accepted records.                                                   |
| SDK, EF Core, tests and operations                  | [SDK pin](../global.json), [target framework](../Directory.Build.props), project files, [Dockerfile](../Dockerfile), [Compose](../docker-compose.yml), [CI definition](../../.github/workflows/ci.yml). Defined workflows are not described as executed CI/Docker results.         |
| Setup defaults and seed counts                      | [Demo launcher](../scripts/run-demo.sh), [seed](../src/CivicPay.Infrastructure/DemoSeeder.cs), [ignored files](../.gitignore), [Docker exclusions](../.dockerignore). SQLite, local Development access, empty-database seed and persistent data documented.                        |
| API examples and sample reconciliation totals       | [Valid fixtures](../data/valid/), API controllers, executed curl results below. Sample account and payment arithmetic matches the importer.                                                                                                                                        |
| Case study and unsupported behavior                 | [Riverbend requirements](case-study/RIVERBEND_REQUIREMENTS.md). Service-specific partial rules remain a gap; acceptance/go-live are planned. No real Catalis process or architecture claimed.                                                                                      |

Removed machine-specific temporary SDK instructions from the public README. Both READMEs now use correct relative documentation/image links. At the time of this review, the clone URL was a placeholder because no Git remote was configured; no public clone or GitHub publication had been executed. See the publication note below for the subsequent clone URL update.

## Executed clean-source checks

Created a temporary source copy excluding `bin`, `obj`, test outputs, `.env` and local databases. Used .NET SDK 10.0.401 and the available NuGet cache; this was not an uncached remote clone.

| Check                                             | Actual result                                                                                                                      |
| ------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------- |
| Restore; complete Release solution build          | Passed; 0 warnings, 0 errors                                                                                                       |
| Full .NET tests                                   | 27 unit + 44 integration passed; 1 conditional SQL Server test skipped                                                             |
| Dashboard helper tests                            | 4 passed using Node.js 24.13.0                                                                                                     |
| Documented demo script                            | Started on port 5081 with an isolated SQLite file; original port-5080 dashboard left running                                       |
| Exact README curl blocks, changing only base port | Account import 201; new payment 201; exact retry 200, same ID; below-minimum request 422 with documented message                   |
| Payment persistence                               | One `README-PAY-001` record; CAD 1,000 opening balance became 975 once and stayed 975 after replay/failure                         |
| Documented Python smoke command                   | Passed against port 5081, including valid/invalid/repeated imports, reconciliation, replay, malformed JSON and Swagger             |
| Runtime health, Swagger and dashboard             | HTTP 200; SQLite health `Healthy`; Swagger paths and dashboard HTML present                                                        |
| Local Markdown/Mermaid browser preview            | All 3 diagrams rendered as SVG, all embedded screenshots loaded, quick-start anchor navigated, no captured console errors/warnings |
| Backend / original .NET files                     | SHA-256 comparison: all 43 baseline files unchanged                                                                                |

The only setup code edit makes `scripts/run-demo.sh` honor a supplied `ASPNETCORE_URLS`; its default remains `http://127.0.0.1:5080`. No application validation, API, database, dashboard or payment behavior changed.

Evidence: [restore](verification/readme-restore.log), [build](verification/readme-build.log), [.NET tests](verification/readme-tests.log), [dashboard tests](verification/readme-node-tests.log), [executed checks](verification/readme-checks.json), [smoke results](verification/readme-smoke.json), [local opening preview](screenshots/readme-preview.jpg), [rendered architecture](screenshots/readme-architecture-preview.jpg).

## Not executed

At the time of the local README review, Docker startup/build, SQL Server migrations/runtime, GitHub Actions, remote cloning, live GitHub rendering, Windows/Git Bash/WSL runs and production deployment had not been verified. Publication checks below supersede the clone, GitHub rendering and Docker build/Compose validation boundaries. The preview is a local Markdown/Mermaid render, not a GitHub screenshot. Earlier [dashboard verification](UI_UX_VERIFICATION.md) records its separate browser checks and limitations.

## Publication preparation

The clone command now points to `https://github.com/tanmay-satija/CivicPay.git`. Personal absolute workspace paths in saved verification output were replaced with `<workspace>` before publication; test results are unchanged. Publication does not imply additional runtime verification.

Published as a public repository on October 6, 2026. A fresh GitHub clone matched all 130 reviewed files byte-for-byte at commit `96e92b41a42bd6140379800319f0f05fcdb7c795`. The live repository showed the README, disclaimer and dashboard images; the embedded dashboard screenshots loaded successfully. The [first GitHub Actions run](https://github.com/tanmay-satija/CivicPay/actions/runs/37437547687) passed restore, Release build, .NET tests, JavaScript syntax/helper tests, Compose configuration validation and Docker image build. Its opt-in SQL Server job was skipped. Container startup, SQL Server migrations/runtime, Windows and production deployment remain unverified.
