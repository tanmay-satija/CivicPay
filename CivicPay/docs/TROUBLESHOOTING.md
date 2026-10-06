# Troubleshooting runbook

Start with response errorCode and X-Correlation-ID. Search structured API logs for that ID, then inspect IntegrationEvents and ErrorLogs or Error explorer. Never send stack traces to API callers. JSON logs include internal diagnostics for operators; preserve them only in trusted local tooling.

| Scenario | Expected evidence | Diagnosis and action |
|---|---|---|
| Unsupported type | 422 UNSUPPORTED_PAYMENT_TYPE | GET enabled types; fix legacy mapping or review config |
| Duplicate changed payload | 409 DUPLICATE_REFERENCE | Compare original transaction; do not invent a new reference to hide a retry |
| Invalid account | 404 ACCOUNT_NOT_FOUND | Inspect /api/monitoring/accounts with municipality; migrate account first |
| Wrong account type | 422 ACCOUNT_TYPE_MISMATCH | Map account to canonical service correctly |
| Minimum / partial rule | 422 BELOW_MINIMUM / PARTIAL_PAYMENT_NOT_ALLOWED | Compare current config and outstanding balance |
| Malformed request | 400 INVALID_REQUEST | Check JSON, decimal and ISO calendar date serialization |
| Database validation | 409 DATABASE_CONFLICT for unique/concurrency collisions; 500 INTERNAL_ERROR for unexpected database failures | Inspect operator logs, constraints and migrations; retry an uncertain payment with its same reference |
| Failed migration row | ImportRecord.ErrorCode and Message | Correct source row; preserve stable reference; inspect reconciliation completeness |
| Invalid config | 422 INVALID_CONFIGURATION | Check supported currency/types, minimum and required name/code |
| Stale config | 409 CONFIGURATION_CONFLICT | Reload version; review changes, then resubmit |
| Auth failure | 401 UNAUTHORIZED | Set X-Api-Key; dashboard API access key resets on reload |
| SQL startup failure | No listener; logs show initialization failure | Check password, healthcheck, connection string, image architecture and migrations |

## Reproduce safely

The invalid fixture supplies unknown municipality, unsupported type, blank account, negative amount, malformed amount/date and duplicate conflicts. Import valid accounts/payments first for the duplicate scenarios. Unit tests directly verify rule failures. Integration tests include a negative-balance write to verify a database CHECK constraint without exposing a destructive endpoint.

## Operational checks

```bash
docker compose ps
docker compose logs --tail=100 api
curl http://localhost:5080/api/health -H "X-Api-Key: $CIVICPAY_API_KEY"
```

Connectivity failure returns 503 from the health endpoint when the application is already running. Initialization failure stops startup instead. Telemetry persistence errors do not undo accepted records; use transactions and import records as the source of business outcomes. Metrics may be incomplete during database faults.

Processing batch after a crash: inspect reported rows and existing references; retain expected source count, then rerun the same source file. Already accepted payments skip; changed payloads conflict. Existing account balances must not be reset. Failed batches remain as evidence, not silently deleted.

SQLite after schema changes: stop the local application and delete its **synthetic** civicpay.db plus sidecars, then restart the demo to recreate/seed. SQLite does not execute the SQL Server migration. SQL Server schema changes must use new EF migrations.

Compose on ARM: SQL Server's x86-64 image may fail under emulation. Use a compatible x86-64 Docker host or separately provisioned SQL Server; the SQLite dashboard/demo remains available. Do not claim SQL Server is verified from a SQLite run.

Common .NET build issue: ensure .NET 10 SDK is on PATH. Use `dotnet --info`, restore tools/packages, and check global.json. API fails fast when the SQL connection or a sufficiently long API key is missing, or when SQL Server MARS is enabled. Use scripts/run-demo.sh for the explicit local alternative.
