# Municipal implementation guide

Audience: implementation specialist onboarding a fictional municipality. All customer names, accounts and amounts are synthetic. This exercise models configuration, migration, integration testing and handover, not actual payment acceptance.

## 1. Prerequisites

.NET 10 SDK; Git; Docker Engine/Compose for SQL Server deployment; Python 3 for optional smoke verification. SQL Server's Linux image targets x86-64. Apple Silicon needs a compatible x86-64 container environment or an external SQL Server; this repository's SQLite mode supports local demonstrations without proving SQL Server behavior.

## 2. Environment setup

Clone, enter CivicPay, run `dotnet restore`, `dotnet build`, `dotnet test`. For the SQL path, copy `.env.example` to `.env`; generate unique local secrets, e.g. `openssl rand -hex 24` for the API key. SQL password must meet SQL Server complexity rules; avoid semicolons in the local password because it is composed into a connection string. Do not publish .env.

`docker compose up --build -d` starts SQL Server and waits for its healthcheck before starting API. Inspect `docker compose logs api` for startup failures. Dashboard is http://localhost:5080; enter the generated API key through API access. Swagger is /swagger. Set CIVICPAY_API_KEY in your shell for curl/smoke calls; Docker .env does not automatically export variables to the host shell.

For local SQLite development, `bash scripts/run-demo.sh`. This explicitly enables unauthenticated loopback demo access. Use this mode only with synthetic data.

## 3. Client discovery and configuration

Record municipality code, enabled service types, currency, partial-payment policy and minimum. Review sample configs and submit POST /api/municipalities or use the dashboard. Map each legacy payment code to the canonical catalogue. Document that overpayments are always rejected and minimums have no final-balance exception. Retrieve the configuration to verify accepted types and version.

## 4. Database initialization

Compose sets Database__Initialize=true to apply committed SQL Server migrations and Database__Seed=true for fictional seed data. Alternatively use `dotnet tool restore` then:

```bash
# Export your own ConnectionStrings__CivicPay first.
dotnet ef database update --project src/CivicPay.Infrastructure --startup-project src/CivicPay.Infrastructure
```

`sql/schema.sql` is generated from that same migration and is an alternative DBA-review deployment artifact. It includes EF migration history. Do not apply both schema initialization paths independently. `sql/seed.sql` is an alternative smaller SQL-only seed, for an empty database after migration; do not combine it with application seeding.

## 5. Account migration

Confirm opening balances precede migrated payments. Import data/valid/accounts.csv, inspect all three rows, retain batch ID and reconciliation. Exercise data/invalid/accounts.csv and review row rejections. Existing accounts are never reset by imports.

## 6. API integration

Use the integration guide and Swagger to send a new simulated payment. Save its ID/reference; retry the exact payload and verify replayed=true with the same ID. Change the amount under that reference and verify a 409. Test enabled and disabled types, unknown account, negative amount, malformed JSON and a partial payment for Pine Valley.

## 7. Testing

Run `dotnet test`. SQLite integration tests isolate temporary databases and exercise controllers, persistence, concurrency, CSV and responses. SQL-specific verification is conditional:

```bash
# Supply a connection with a CivicPayTests_<suffix> catalog and create/drop database permission.
# The test creates/deletes its own unique database; the supplied catalog is untouched.
export CIVICPAY_TEST_SQLSERVER='<your disposable SQL Server connection string>'
dotnet test tests/CivicPay.IntegrationTests --filter FullyQualifiedName~SqlServerTests
```

The SQL test rejects database names without CivicPayTests_ prefix, applies migrations, checks connectivity, seeds data and verifies a check constraint. Do not supply a shared database. Run `python3 scripts/smoke.py` against a running API; it writes synthetic records and is repeatable. See VERIFICATION.md for what was actually verified during project creation.

## 8. Reconciliation

Import valid payments, verify zero difference and RECONCILED. Import invalid payments, inspect every rejected/skipped row and the incomplete amount flag. Correct source mapping or configuration deliberately, then import a corrected synthetic file with stable references. Do not interpret a partial source sum as a complete source total.

## 9. Demo go-live review

Complete IMPLEMENTATION_CHECKLIST.md. Require reconciled valid imports, understood invalid scenarios, stable references, passing available tests, known database/Docker verification gaps and documentation handover. "Go live" here means approving a portfolio demonstration only.

## 10. Troubleshooting and handover

Record correlation IDs, batch IDs, expected and actual results. Use Error explorer, JSON logs and sql/failed_transactions.sql. Provide rules, API mapping, migration reports and unresolved limitations to the fictional client. Never include actual personal or financial records.
