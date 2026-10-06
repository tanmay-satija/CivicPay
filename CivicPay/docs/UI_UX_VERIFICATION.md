# Dashboard UI/UX verification

Completed October 6, 2026 (America/Edmonton and UTC). The application is a synthetic portfolio sandbox.

## Scope

Redesigned the six dashboard views, including health summaries that name affected clients in descending failure order, four overview KPIs, accepted-record volume, failure reasons, municipal performance, error investigation, migration reports, filtering, pagination, loading/empty states and responsive navigation. Added four pure diagnostic-rendering tests and a local synthetic data workflow. Updated both READMEs and added the diagnostic tests to portable CI.

**Backend behavior was preserved.** SHA-256 comparison confirmed that all 43 original backend and .NET test/configuration files are unchanged. Controllers, services, database mappings, migrations, security middleware, API contracts and Docker files were not edited.

## Executed build and test checks

| Check | Executed result |
|---|---|
| Complete solution, Release build | Passed: 0 warnings, 0 errors |
| .NET unit tests | 27 passed |
| .NET integration tests | 44 passed, 1 SQL Server test skipped |
| Dashboard helper tests | 4 passed: quoted/JSON credentials, bearer/connection details, .NET stack suppression and HTML escaping |
| Final publish and application startup | Passed; running on `http://127.0.0.1:5080` with an isolated SQLite database |
| Health endpoint | HTTP 200; `Healthy`, `Sqlite`, `synthetic=true` |
| Served dashboard assets | HTTP response bytes matched all three source assets |
| Swagger | JSON returned HTTP 200 with paths; browser rendered controller operations and schemas |
| Fresh final browser load | Overview loaded and connected; no captured console errors or warnings |

Executed from the `CivicPay` directory with the temporary .NET 10 SDK:

```bash
dotnet build CivicPay.sln --no-restore -c Release
dotnet test CivicPay.sln --no-build -c Release
dotnet publish src/CivicPay.Api -c Release --no-build
node --test tests/dashboard/diagnostics.test.cjs
```

Evidence: [build output](verification/ui-build.log), [.NET test output](verification/ui-tests.log), [dashboard helper test output](verification/ui-node-tests.log), [executed API workflow](verification/ui-api-checks.json), [final runtime checks](verification/ui-runtime-final.json).

An initial browser session showed the older dashboard document and a missing-element JavaScript error while assets were being replaced. Fixed this startup mismatch with versioned stylesheet/script URLs and a markup compatibility guard. Executed a browser regression check using a temporary copy of the original document: the current script displayed a reload notice, Refresh opened the current overview, the API connected, and neither load produced captured console errors or warnings. Removed the temporary fixture afterward. These final checks do not erase the earlier observation.

## Executed browser workflows

All six views were navigated in the Codex in-app browser. Verified:

- Municipality filtering narrowed KPIs, errors and performance to Pine Valley. The Today preset selected the current UTC date. Reset restored all municipalities and the 28-day window.
- Searching the municipal code `PINE-VALLEY` returned only Pine Valley errors. Searching the actual correlation ID `f659f89e4936491397881b53f075776f` returned one Pine Valley failure. Category filtering isolated conflicts. The inspector displayed timestamp, municipal context, error code, inferred category/process and the public diagnostic message. Copying the ID produced the exact ID in the browser clipboard.
- A successful browser CSV upload accepted two accounts: CAD 1,275 source, CAD 1,275 imported, CAD 0 difference, RECONCILED. A malformed-header upload displayed `INVALID_CSV_HEADER` with a correlation ID and retry guidance. Final error handling refreshed the visible error count.
- Batch inspection showed rejected and skipped rows, an unparseable amount and an incomplete source-total warning. The 56-row account batch paginated from rows 1–50 to 51–56; the second page displayed CSV source row numbers 52–57.
- A browser payment of CAD 25 succeeded. An identical retry returned the same transaction ID; changing the amount to CAD 30 returned `DUPLICATE_REFERENCE`. Search showed one stored accepted record. A final failed submission immediately refreshed the failed-payment count. HTTP inspection confirmed the balance changed once, from CAD 4,459.50 to CAD 4,434.50.
- Saving Pine Valley's existing configuration succeeded and retained its rules.
- A future reporting window showed no accepted volume or recorded errors, an undefined success rate (`—`) and municipal `No requests` status. Today remained independently labelled as today's UTC count.
- An inverted date range displayed a clear validation warning, retained the previous snapshot and kept `API connected` intact.
- Initial loading displayed skeletons and a disabled refresh button. Refresh completed and advanced the explicitly UTC-labelled snapshot timestamp.
- Desktop rendering was inspected at the normal desktop viewports (captures included 1265×712 and 1280×720 pixels; final desktop overview is 1456×778 pixels at the current default window size). Responsive layouts were inspected at 1024×768, 390×844 and 320×740. At both phone widths the document had no horizontal overflow. The narrow error dialog fit at 288 pixels. The mobile menu opened, routed to a view and closed. Import tables retained their overflow inside a focusable table region (737-pixel table within a 286-pixel container at the narrow width).

A single right-arrow attempt did not demonstrate table scrolling before the next layout change; keyboard/touch horizontal scrolling is not claimed as verified. A later attempt to repeat phone sizing after the final text/cache refinements timed out in the browser viewport control; the earlier responsive checks above were executed successfully. The final preview retained desktop dimensions. No browser outage simulation or automated accessibility audit was performed.

## Executed API and reconciliation workflows

The populated sandbox uses the unchanged application seed plus **executed HTTP requests**, not invented request history or timings. `scripts/populate-ui-demo.py` was executed on a fresh seeded database.

| Scenario | Executed result |
|---|---|
| Valid account migration | 3 accepted; CAD 1,575 source/imported; RECONCILED |
| Valid payment migration | 3 accepted; CAD 250.50 source/imported; zero difference; RECONCILED |
| Repeated payment migration | 0 newly imported, 3 skipped; CAD 250.50 difference; WARNING |
| Invalid payment rows | 7 rejected, 1 skipped; known source CAD 342; imported CAD 0; incomplete source total; WARNING |
| Unmapped Pine Valley payment batch | 2 rejected; CAD 200 difference; FAILED |
| Larger account migration | 56 accepted; CAD 17,780 source/imported; RECONCILED |
| Malformed CSV header | HTTP 400, `INVALID_CSV_HEADER`; no batch created |
| Synthetic REST payments | 18 new accepted payments across the three municipalities |
| Identical REST retry | HTTP 200, same transaction ID, `replayed=true` |
| Changed payload under existing reference | HTTP 409, `DUPLICATE_REFERENCE` |
| Unmapped accounts / unsupported services | HTTP 404 / 422 with safe structured error responses |
| Persisted reconciliation reads | Reports matched the returned import results |

The later browser workflow added two accounts and one accepted payment. The full snapshot has 190 accepted records; the 28-day window contains 187. Counts change when subsequent requests are executed. Screenshots show the actual snapshot at their displayed update time.

## Reporting definitions and API limits

- **Transactions Today:** unique accepted records created today in UTC, scoped to the selected municipality. This remains a today metric when another reporting window is selected.
- **Success Rate:** successful executed API requests divided by all executed API requests in the selected window. This includes request retries and successful import requests even when individual CSV rows were rejected. Monitoring/health polling and fabricated seed telemetry are excluded by the existing API. No requests displays `—`.
- **Failed Transactions:** rejected `POST /api/payments` requests, including retries. Failures do not create accepted transaction rows.
- **Active Municipalities:** configured clients with at least one enabled service, scoped to the municipality filter. The system has no separate online/offline client flag.
- **Transaction volume:** accepted records grouped by UTC creation date, using the latest 200 matching records and at most 28 days. The chart states its snapshot limit and marks a partial series when authoritative totals exceed the loaded records. It does not relabel API request volume as transaction volume.
- **Failure reasons:** top four codes from the latest 100 matching error records. Labelled synthetic seed examples can appear here; they do not contribute to measured request metrics.
- **Municipality performance:** metrics read from the existing municipality-filtered monitoring endpoint, with at most three requests in flight. Failed request counts indicate attention; an empty sample indicates unknown health. A failed client-metric read displays unknown values.
- **Migration summaries:** latest 30 matching batches. Source minus **newly** imported amount equals difference. Skipped duplicates add no new imported amount. Unparseable amounts or an interrupted batch can make the source total incomplete; the inspector explains that known amounts alone are displayed.
- **Diagnostics:** error category and process are inferred from the code and labelled accordingly. The existing error response does not expose an endpoint, so the inspector says it is unavailable and directs authorized investigation to correlation-based server logs. No endpoint or stack trace is fabricated.
- **Recent changes:** latest recorded event per process (accepted transaction, migration batch, executed request failure). The existing API has no timestamped configuration history; that limitation is visible.

## Not verified

SQL Server execution remained unavailable, so its conditional test was skipped. Docker was not run for this UI pass, and the GitHub Actions jobs were not triggered. Browser verification used the Codex in-app browser; Chrome, Firefox, production deployment, screen-reader auditing, touch gestures, load testing and network-outage recovery were not executed. The local runtime used explicit Development open demo access, not a production authentication deployment.

## Captured screenshots

- [Complete overview](screenshots/ui-overview.jpg)
- [Desktop overview](screenshots/ui-overview-desktop.jpg)
- [Safe error investigation](screenshots/ui-error-investigation.jpg)
- [Imports and reconciliation](screenshots/ui-imports.jpg)
- [Batch diagnostics](screenshots/ui-batch-investigation.jpg)
- [Municipality configuration](screenshots/ui-municipalities.jpg)
- [Phone layout](screenshots/ui-mobile.jpg)

The README images link to these executed browser captures. Their data is synthetic and their counters reflect real sandbox requests.
