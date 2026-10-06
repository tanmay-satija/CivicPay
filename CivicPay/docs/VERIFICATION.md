# Verification report

For the subsequent technical audit and current results, see [TECHNICAL_AUDIT.md](TECHNICAL_AUDIT.md). The table below records the original creation run, not a substitute for executing the audit checks.

Verified on October 5, 2026 (America/Edmonton) on macOS ARM64. SDK 10.0.401 was downloaded from Microsoft's official installer into /tmp. No Docker executable or SQL Server service was available. SQL Server was not replaced or emulated by SQLite in the verification claims below.

## Executed evidence

| Acceptance check | Actual result |
|---|---|
| Restore dependencies | Passed; initial vulnerable transitive dependencies updated; restore no longer reported vulnerability errors |
| Build entire solution | Release build passed, zero warnings/errors |
| Automated tests | 27 unit + 21 integration tests passed; 1 SQL Server test explicitly skipped |
| Start application | Running Release API on 127.0.0.1:5080, explicit Development SQLite mode |
| Major API endpoints | Configuration create/read/update, enabled types, payment create/read/replay/conflict, imports/records/reconciliation, monitoring/accounts, health exercised by tests/live smoke |
| Valid account import | 3 imported; CAD 1575 source/imported; zero difference; RECONCILED |
| Valid payment import | 3 imported; CAD 250.50 source/imported; zero difference; RECONCILED |
| Repeated payment import | 3 skipped; zero new amount; CAD 250.50 difference; WARNING |
| Invalid payment import | 7 rejected + 1 skipped; CAD 342 parseable source amount; incomplete source flag; WARNING |
| Duplicate protection | Sequential exact replay, changed-payload 409, parallel retries and independent municipality references tested |
| Database validation | SQLite negative-balance CHECK constraint tested; composite/unique mappings present in generated SQL |
| Dashboard | All six views rendered; configuration save, synthetic payment submission, CSV upload, rejected-row inspection, municipality/outcome/date filters verified in browser |
| Browser diagnostics | No console warnings/errors reported during the inspected dashboard session |
| Swagger | JSON generated successfully, payment schema and security definition/requirement tested; UI served |
| Published artifact | dotnet publish passed; separately started published app and checked health, HTML, JS, CSS and Swagger using SQLite |
| Migration consistency | EF has-pending-model-changes reported no changes since last migration |
| SQL schema artifact | Generated idempotent SQL Server migration script with constraints/indexes; not executed against SQL Server |
| Script syntax | node --check dashboard/app.js, bash -n scripts/run-demo.sh, Python compile check passed |
| Documentation | Reviewed commands, provider distinctions, seed counts, retry and reconciliation semantics against implementation/results |
| Docker / SQL Server | Not executed; required runtime/service unavailable |

Machine-readable evidence: [test counters](verification/test-results.json), [live smoke results](verification/smoke-results.json), [second independent smoke run](verification/smoke-repeat-results.json). Smoke runs add suffixes to fixture accounts/references so successive runs remain isolated and repeatable; each run separately retries its own payment file. Screenshots in screenshots/ reflect the live synthetic session, not hard-coded metric mockups.

## Commands executed

```bash
dotnet restore CivicPay.sln
dotnet build CivicPay.sln --no-restore -c Release -m:1 -nodeReuse:false -p:UseSharedCompilation=false
dotnet test CivicPay.sln --no-build -c Release --logger 'trx;LogFileName=verification.trx'
dotnet ef migrations script --idempotent --project src/CivicPay.Infrastructure --startup-project src/CivicPay.Infrastructure --no-build --output sql/schema.sql
dotnet ef migrations has-pending-model-changes --project src/CivicPay.Infrastructure --startup-project src/CivicPay.Infrastructure --configuration Release --no-build
dotnet publish src/CivicPay.Api -c Release --no-restore -o /tmp/civicpay-publish
python3 scripts/smoke.py
```

The installed SDK used /tmp/civicpay-cli for DOTNET_CLI_HOME and /tmp/civicpay-nuget for NuGet storage. Process communication and loopback networking required execution outside the restricted sandbox. The SDK is temporary; normal future use requires .NET 10 on PATH. No system-wide Docker installation or real database credentials were introduced.

## Remaining external verification

The project implementation is present, but the complete SQL Server/Docker acceptance gate is **not yet satisfied**. On a compatible host:

1. Install Docker/Compose with a SQL Server-compatible x86-64 runtime, or supply an external SQL Server.
2. Copy .env.example to .env and set generated local secrets.
3. Run `docker compose config --quiet`, then `docker compose up --build -d`.
4. Inspect SQL healthcheck and API logs; verify /api/health with X-Api-Key.
5. Confirm EF migration history and actual SQL constraints/indexes.
6. Set CIVICPAY_TEST_SQLSERVER to a disposable database named CivicPayTests_<suffix> and run the conditional test. That test covers migration/connectivity, seed data, payment replay/conflict, CSV reconciliation and a SQL Server CHECK constraint; it creates/deletes its own unique CivicPayTests_<guid> database, leaving the supplied database untouched.
7. Export CIVICPAY_API_KEY, run scripts/smoke.py against the SQL-backed API, and review all reconciliation results.
8. Open the dashboard, connect with API access, exercise all six views and Swagger's Authorize/Try it out workflow.
9. Execute the reporting queries against CivicPay (not master) and compare them with API totals.

Provider-specific collation, decimal storage, locks, SQL Server migration execution, SQL scripts, image build/startup and Compose healthcheck behavior remain unverified until those steps run. CI workflow presence is not evidence of a successful GitHub Actions run.

## CI setup

The root .github/workflows/ci.yml always runs portable tests and Docker syntax/build steps on Ubuntu; no secret is required for that job. Its optional SQL job runs only when repository variable CIVICPAY_SQL_TESTS=true and a strong secret CIVICPAY_CI_SQL_PASSWORD is supplied. This prevents an unconfigured secret from breaking the portable job. No CI run was dispatched or claimed during creation.

## Known design limits

- A single shared API key and local open demo mode are portfolio conveniences, not full identity/role management.
- Analytics aggregate portfolio-sized datasets in memory; there is no production retention/background pipeline.
- External references now use binary, case-sensitive equality on both providers; the SQL Server collation migration is generated but has not been executed locally.
- Processing batches can remain after a hard crash; expected row count exposes incompleteness, but no automated recovery worker exists.
- Telemetry writes are best effort and can miss requests during database faults; business records are authoritative.
- Successful import HTTP requests can contain rejected rows. Request success rate measures HTTP outcomes; import row outcomes are inspected separately.
- Configuration version also advances on payments and new account imports to guard concurrent rule changes; reload before editing stale settings.
- Amounts cannot exceed balances; final payments still obey the minimum. There is no settlement or refund workflow.
