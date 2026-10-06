# CivicPay technical audit

Audited October 5, 2026 in America/Edmonton; execution timestamps cross into October 6 UTC. The review covered the four application projects, both test projects, migrations and snapshot, dashboard, CSV fixtures, SQL scripts, Docker/Compose, CI, configuration, scripts and documentation. Changes repair existing behavior; no business features were added.

The original solution built cleanly and passed 48 portable tests. That did not establish correctness: the targeted before-fix run reproduced 11 failing regression cases. A twelfth failure was an incorrect account lookup in the new test, which was corrected and is not counted as a product defect. The final solution builds with zero warnings/errors and passes **71 tests: 27 unit and 44 integration**. **One SQL Server test is skipped.**

The portable implementation has passed the checks below. SQL Server deployment acceptance remains open: neither its migrations nor its runtime behavior were executed against a server here.

## Findings and fixes

| Severity | Finding | Correction and evidence |
|---|---|---|
| High | A full-balance retry could read no reference, then read the balance after the original committed and incorrectly return OVERPAYMENT. | Recheck the reference when current-state validation fails, and after concurrency collisions. A deterministic interleaving reproduces the old failure; the corrected test replays the original ID. Eight simultaneous live full-balance requests produced one 201, seven replays and one debit. |
| High | Database initialization without synthetic seeding left PaymentTypes empty. Creating a valid municipality silently saved zero accepted types. | Required catalogue initialization is separate from demo data; a forward SQL Server migration inserts missing reference codes. Configuration refuses incomplete catalogue data. Fresh unseeded SQLite startup, configuration, account import and payment/replay executed successfully. SQL migration execution is pending. |
| High | Account imports did not participate in configuration concurrency, permitting a type-disable check to race with account creation. | New account imports advance the same configuration version used by rule changes/payments. Version advancement is tested. Actual SQL Server account-import/type-disable interleavings remain unverified. |
| High | Broad DbUpdateException catches mislabeled foreign-key errors and outages as 409 and retried them. | Retry/map only known unique-key and optimistic-concurrency failures. Unexpected failures reach the safe 500 handler and operator log. Foreign-key failure preserves the balance; an injected HTTP database failure is attempted once, returns INTERNAL_ERROR without its diagnostic text, and persists its correlation ID. |
| High | SQL Server MARS can disable the savepoints required for safe recovery inside an import row transaction. Custom SQLite connections could request disabled foreign keys. | API startup rejects MARS before connecting; API SQLite connections always enable foreign keys. Both guards have executed regressions, including an actual rejected foreign-key insert with Foreign Keys=False supplied. MARS transaction behavior on a real SQL Server is not claimed as tested. |
| Medium | A failed import's cleanup could throw another exception and hide the original failure, or wait indefinitely after cancellation. | Preserve the original exception, bound cleanup to five seconds, and log cleanup failure. Injected reporting and cleanup failures prove preservation of the original error and rollback of the payment/balance. A failed reporting write marks the batch Failed when cleanup is available. |
| Medium | Imports accepted invalid UTF-8 through replacement characters, counted characters instead of bytes in the service, and allocated every logical row before checking the row limit. | Strict UTF-8 decoding, incremental 2,000,000-byte limit, and parsing at most 5002 logical records before rejecting excess rows. Invalid encoding, multibyte byte overflow, oversized multipart/file bodies and 5001 data rows were rejected; no business import occurs for file-level rejection. |
| Medium | Oversize-file status and framework 404/405/415 responses contradicted the documented JSON error contract. | Consistent ApiError envelopes and correlation IDs, preserving 413 for size limits. Executed structured 400/404/405/409/413/415/422 checks, plus the injected 500 test. |
| Medium | Pagination arithmetic overflowed for large valid integer page values. | Calculate offset in long and reject out-of-range offsets before querying. Both paginated endpoints return INVALID_PAGINATION (400) for overflowing input. Ordinary row pagination was exercised live. |
| Medium | Monitoring loaded unrelated rows, queried each already-loaded batch again, and took 30 batches before applying outcome filters. | Apply municipality/date/applicable outcome predicates in the database; reuse loaded records for reconciliation; filter outcomes before the display limit. Tests cover UTC offset/date boundaries and an older rejected batch behind 31 newer successful batches. Metrics still aggregate matching records in memory; this is not production-scale analytics. |
| Medium | Seeded integration events fabricated request counts and response times. | Seeder no longer invents HTTP events; analytics and SQL request reports exclude historic synthetic-* events. Synthetic transactions and labelled error examples remain demonstration data. A regression confirms seed history contributes zero measured requests/latency. |
| Medium | Unbounded invalid municipality context could prevent SQL telemetry persistence. Cancellation was treated as an internal error, and errors after response start did not abort the connection. | Bound persisted municipality/method fields, use a telemetry timeout, handle client cancellation separately and abort incomplete responses after an exception. Correlation/error persistence executed; real network-disconnect and telemetry-outage behavior was reviewed but not executed. |
| Medium | The configuration dialog retained its old version after saving; a second save always conflicted. New-client dialogs similarly retained create mode. | Retain the returned configuration/version and lock the created code. Browser editing saved minimum 5 → 6 → 5 in the same dialog, with new versions and successful responses. Creation/update API behavior is covered by integration/live tests. |
| Medium | Payment UI loaded only the first 200 accounts and could submit an undefined account when none were eligible. | Retrieve all pages for the selected municipality and show a clear empty state. Browser testing selected the only funded account at position 202, submitted a payment, replayed it and displayed a rejected overpayment. An empty municipality displayed no submit form. |
| Medium | Async modal results could write into a different open form; slow refresh results could render under changed filters. | Associate submit results with their original connected form, safely handle invalid API response JSON, and discard refresh results whose filter snapshot changed. Normal form/filter flows executed; exhaustive latency/modal-close interleavings were not tested. |
| Medium | SQL tests could migrate/delete an existing database simply because its name began CivicPayTests_. | Generate a unique owned database name, preserve the supplied catalog, and explicitly disable MARS. Reviewed and compiled; real create/drop execution is pending SQL Server availability. |
| Medium | SQL-only seed inserted catalogue codes unconditionally and would conflict with independently initialized reference data. Failure handling could leave a transaction open. | Insert only missing codes; wrap seed in TRY/CATCH with rollback. Reporting query returns zero rather than null for clients without requests. SQL artifacts reviewed and migration script regenerated, but not executed against SQL Server. |
| Low | Unit tests referenced the entire API and WebApplicationFactory; integration cleanup cleared every SQLite pool. Compressed frontend source made review difficult. | Pure unit tests reference Application only; cleanup targets the test's pool; source formatting improved. All solution tests pass. |
| Low | Git/Docker ignores missed environment variants and Docker SQLite sidecars; docs overstated bounded analytics and described obsolete reference/seed/test behavior. | Ignore environment variants while preserving .env.example; exclude SQLite data/sidecars from the image context. Corrected contracts, limits, seed metrics, configuration versions, test safety, reference comparison and run-directory instructions. |

The savepoint risk is based on the reviewed transaction flow and Microsoft's [EF Core transaction documentation](https://learn.microsoft.com/en-us/ef/core/saving/transactions): MARS prevents EF savepoints, including when no simultaneous reader is active. The startup guard is executed evidence; SQL Server rollback/locking validation still requires its server.

## Other review results

- No baseline restore/build failure or broken placeholder endpoint was found. The defects above were behavioral, safety and documentation problems that the original passing suite missed.
- No exposed real credential was found in reviewed project files. .env.example contains instructions/placeholders; the test API key is explicitly synthetic. Pattern scans found no private keys, AWS access-key identifiers or recognizable live service-token patterns. This is a working-tree review, not a claim of exhaustive secret detection. There are no Git commits/history in this repository to audit.
- NuGet's direct/transitive vulnerability audit reported no vulnerable packages in the six projects at execution time. Evidence: [audit-packages.json](verification/audit-packages.json).
- EF mapping retains atomic transaction/balance writes, optimistic account/configuration tokens, tenant/reference uniqueness, composite account/type foreign keys and check constraints. These protections were exercised with SQLite; provider-specific SQL Server behavior is pending.
- No repository-wrapper framework, background worker, authentication feature, payment provider or other new product functionality was introduced.

## Executed final verification

| Check | Actual execution/result |
|---|---|
| Restore | Solution restore succeeded. |
| Full build | All six projects built in Release; zero warnings, zero errors. |
| All tests | 27 unit + 44 integration passed; zero failures; one conditional SQL Server test skipped. 23 regression cases were added. |
| Publish/run | Final Release API published, started on 127.0.0.1:5080 with a synthetic SQLite database. Published API DLL hash matches the tested build. |
| Configuration | Create/read/update, Location lookup, enabled services and stale-version 409 executed live. |
| Payments | Create/read, exact replay, conflicting replay, case-sensitive references, eight concurrent full-balance retries and two competing distinct payments executed live. Competing payments produced one acceptance and one OVERPAYMENT; no negative balance or lost debit. |
| Successful imports | Supplied account fixture: 3 imported, CAD 1575, zero difference. Supplied payment fixture: 3 imported, CAD 250.50, zero difference. Both RECONCILED. |
| Failed/mixed imports | Supplied invalid payment fixture: 7 rejected, 1 skipped, CAD 342 parseable source, zero new imported amount, incomplete source flag, WARNING. Independent audit mixed file: 1 imported/4 rejected, CAD 59 source/10 imported/49 difference, incomplete source, WARNING. All-rejected file: FAILED. Conflicting existing account: rejected without overwrite. |
| Duplicate imports | Supplied repeated payment fixture: 3 skipped, zero new imported amount, CAD 250.50 difference, WARNING. Concurrent identical imports also passed in integration tests: one payment imported, one skipped. |
| File rejection | Bad header, malformed quoting, invalid UTF-8, oversized file/multipart and excess row count rejected. |
| Reconciliation | Retrieved reports matched upload results. Independent SQLite queries confirmed source/imported amounts, differences, completeness, account balances and exactly one full-retry transaction. Audit municipality recorded CAD 305 across eight unique payments; balances A=5/B=0/C=0. |
| Monitoring | Municipality, inclusive UTC creation dates and Accepted/Rejected filters executed through APIs and browser controls. UTC-offset boundary and older failed-batch cases passed in tests. |
| Secured unseeded run | Separate final published instance in Production on 5081: no key → 401; valid key → 200; initially zero municipalities/accounts, catalogue of five types; configuration, account import, payment and replay passed; final balance CAD 75. Temporary key removed after stopping this instance. |
| Swagger | JSON paths, request schema and API-key scheme/security contract checked. Browser UI loaded; Try it out GET /api/health executed and displayed 200 Healthy/Sqlite. |
| Dashboard | All six views rendered. Same-dialog repeated saves, account beyond first page, payment/replay/rejection, empty-account state, municipality/outcome/date filters and rejected-row inspection executed. Captured browser warning/error log was empty. |
| EF migration artifacts | New forward migration preserved the initial migration. Idempotent SQL script regenerated; generated SQL drops/recreates the reference index around the collation alteration. EF has-pending-model-changes reported none. No SQL Server migration was executed. |
| Syntax/config files | Node JS syntax, Bash script syntax and Python compilation checks passed. Compose and CI YAML parsed with Prettier. YAML parsing is not Docker Compose validation or a successful CI run. |

Evidence: [test results and names](verification/audit-test-results.json), [live audit](verification/audit-live-results.json), [supplied fixtures](verification/audit-fixture-results.json), [secured startup](verification/audit-secure-results.json), [browser checks](verification/audit-browser-results.json). Original creation evidence remains separate in [VERIFICATION.md](VERIFICATION.md).

Screenshots: [repeated save](screenshots/audit-repeated-save.jpg), [payment replay](screenshots/audit-payment-replay.jpg), [rejected import rows](screenshots/audit-import-errors.jpg), [filtered errors](screenshots/audit-errors.jpg), [Swagger execution](screenshots/audit-swagger.jpg), [overview](screenshots/audit-overview.jpg).

## Not verified

1. **SQL Server connectivity, actual migration execution/upgrade, binary collation behavior, SQL decimal behavior, locking/deadlocks/concurrent rule changes, and standalone SQL reporting/seed queries.** No SQL Server was available. The one conditional SQL Server test is skipped, not passed.
2. **Docker image build, container startup, Compose config validation, SQL healthcheck, volume persistence and restart behavior.** Docker/Compose is not installed. Static review and YAML parsing do not establish runtime correctness.
3. **GitHub Actions execution or Windows/Linux execution.** No CI job was dispatched; portable checks ran on macOS ARM64.
4. **Browser CSV file selection/upload completion.** The in-app browser chooser event timed out; native Codex UI control is denied by tool policy. The upload form and completed batch/row views rendered. Multipart upload, validation and reconciliation were executed through live HTTP and integration tests; browser file-picker completion was not.
5. **Real client network cancellation, response-stream failure, telemetry storage outages, hard process crashes/recovery and production load/retention.** Relevant code was reviewed; injected database/reporting/cleanup failures were executed. No blanket reliability or production claim is made.
6. **Mobile/responsive-browser matrix and exhaustive slow-response/modal-close races.** Desktop browser interactions above were executed.

## Reproduce the portable checks

Run from CivicPay with .NET 10 on PATH:

```bash
dotnet restore CivicPay.sln
dotnet build CivicPay.sln -c Release --no-restore
dotnet test CivicPay.sln -c Release --no-build
bash scripts/run-demo.sh
# In another terminal:
python3 scripts/smoke.py
python3 scripts/audit.py
```

Both HTTP scripts create isolated synthetic records. audit.py optionally accepts CIVICPAY_AUDIT_DATABASE for independent SQLite checks; set it to the exact database file used by the running app. Export CIVICPAY_API_KEY for secured mode and CIVICPAY_URL for another port. SQL Server/Docker verification steps remain in VERIFICATION.md; the SQL test now creates/deletes only its unique generated database.

The audit used SDK 10.0.401 in /tmp/civicpay-dotnet, DOTNET_CLI_HOME=/tmp/civicpay-cli and NUGET_PACKAGES=/tmp/civicpay-nuget. Final published files are in /tmp/civicpay-audit-publish-final. The loopback demo remains running; these temporary paths are not a permanent installation or deployment.
