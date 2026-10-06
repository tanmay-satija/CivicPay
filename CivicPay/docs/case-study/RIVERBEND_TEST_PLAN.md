# Riverbend — test and validation plan

Fictional acceptance plan. **All Riverbend cases below are planned, not executed.** The documentation change does not create passing UAT evidence or implement gap G-01.

## Setup and evidence

Use an isolated database, synthetic seeding disabled, the [prototype configuration](RIVERBEND_SOLUTION_DESIGN.md) and CSV examples from [mapping](RIVERBEND_MAPPING.md). Enable API-key security for security cases. The three starting balances are CAD 2,000.00 / 400.00 / 250.00. Reset to that approved fixture for independent cases; keep state within retry and repeated-import cases. Use fresh references except when intentionally retrying the same transaction.

For each case record environment/provider, commit, configuration version, fixture checksum, UTC execution time, request/reference, expected/actual response, correlation ID, batch/transaction ID, before/after balances and reviewer disposition. Source dates must not be future dates relative to execution.

## Acceptance scenarios

| Test | Requirement | Scenario and expected outcome | Riverbend status |
|---|---|---|---|
| T01 | RB-01 | Create/read configuration and enabled types. Exactly PropertyTax, Utility, Permit. Stale-version update returns 409 `CONFIGURATION_CONFLICT`; no unintended change. | Planned |
| T02 | RB-02 | USD municipal configuration returns 422 `INVALID_CONFIGURATION`. Proposed transform/adapter quarantines a USD source record before CSV/API delivery. A payment request cannot prove currency rejection by adding an undocumented field. | Planned; adapter absent |
| T03 | RB-03 | REST Property Tax payment 100.00 against 2,000.00 returns 201; balance becomes 1,900.00. Repeat through payment CSV on a fresh fixture; row is imported. | Planned |
| T04 | RB-04 | Permit 250.00 against 250.00 succeeds; Permit 100.00 must return 422 `PARTIAL_PAYMENT_NOT_ALLOWED` / a rejected CSV row, leaving balance unchanged. Test both channels while Property Tax partials still succeed. Current prototype instead permits the partial Permit payment. | **Blocked by G-01** |
| T05 | RB-05 | Property Tax 24.99 → 422 `BELOW_MINIMUM`; 25.00 accepted; 2,000.01 → 422 `OVERPAYMENT`; 0, negative or three-decimal amounts → `INVALID_AMOUNT`. Full balance 10.00 under minimum 25.00 remains rejected, exposing the agreed small-balance limitation. Failed requests make no deduction. | Planned |
| T06 | RB-06 | Import `rb-accounts.csv` into empty scenario: 3 imported, CAD 2,650.00 source/imported, zero difference, complete total, `RECONCILED`. Verify all account/service balances. | Planned |
| T07 | RB-06 | Immediately repeat accounts: 3 skipped, zero new accounts, CAD 2,650.00 difference, `WARNING`. Changed existing balance yields `ACCOUNT_CONFLICT`; missing municipality/unsupported type/negative balance rejected per row. Wrong header → 400, invalid UTF-8 → 400, oversize → 413; no batch for these file failures. | Planned |
| T08 | RB-07 | Authorized REST delivery creates/readbacks a payment. Missing account → 404 `ACCOUNT_NOT_FOUND`; enabled but wrong account type → 422 `ACCOUNT_TYPE_MISMATCH`; disabled service → 422 `UNSUPPORTED_PAYMENT_TYPE`; future date → 422 `INVALID_DATE`. Balances unchanged on failures. | Planned |
| T09 | RB-08 | Exact sequential and parallel retries return one transaction ID; one balance deduction. Changed payload under same reference → 409 `DUPLICATE_REFERENCE`. REST replay of a CSV-accepted reference returns 200 with `replayed=true`. | Planned |
| T10 | RB-09 | First `rb-payments.csv` import: 3 accepted, CAD 400.00 source/imported, zero difference, `RECONCILED`; remaining balances total 2,250.00. Repeat: 3 skipped, zero newly imported, CAD 400.00 difference, `WARNING`. | Planned |
| T11 | RB-09 | On a fresh fixture, import Utility rows 50.00 valid, -10.00 invalid, and `oops` unparseable, each with a distinct reference. Expect 1 imported / 2 rejected, known source 40.00, imported 50.00, difference -10.00, `sourceAmountComplete=false`, `WARNING`. Inspect every row; never approve the partial source total. | Planned |
| T12 | RB-10 | Missing/wrong API key → 401; valid key succeeds. Errors expose safe code/message/correlation ID without secrets/stacks. Dashboard filters Riverbend and reports the test failures; health/Swagger load. Deliberate failures remain history, not proof of unresolved incidents. | Planned |
| T13 | RB-10 | Rehearse chosen-provider migration/startup, backup restore and interruption recovery in a disposable environment. Compare expected rows with persisted outcomes, inspect missing rows and references before rerun; no silent balance reset. | Planned; deployment evidence required |

CSV validation failures appear as persisted row outcomes even when upload returns 201. HTTP rejection assertions apply to REST/file-level failures. `Imported` rows are the reconciliation accepted count; skipped duplicates are never new accepted payments.

## Existing baseline coverage

Repository tests already cover generic behavior: `Partial_payment_is_accepted_when_enabled`, `Configured_balance_rules_are_enforced`, `Full_payment_client_rejects_partial_and_accepts_full`, `Accounts_import_reconciles_and_enables_payment`, `Valid_import_reconciles_and_repeat_skips`, `Invalid_rows_are_reported_and_totals_are_honest`, `Parallel_retry_never_creates_two_records`, and `Secured_mode_requires_key_and_documents_it_in_OpenApi`.

See [unit tests](../../tests/CivicPay.UnitTests/RulesTests.cs), [API tests](../../tests/CivicPay.IntegrationTests/ApiTests.cs), [security tests](../../tests/CivicPay.IntegrationTests/SecurityTests.cs), and [regression tests](../../tests/CivicPay.IntegrationTests/AuditRegressionTests.cs). Prior [executed verification](../UI_UX_VERIFICATION.md) records 71 passing .NET tests, four dashboard tests and a skipped SQL Server test. The full-payment test concerns municipality-wide rules; it does **not** satisfy T04's mixed service policy.

## Exit criteria

Require every applicable Riverbend case to pass with retained evidence; resolve G-01; obtain revenue sign-off on balances, minimums and Utility policy; explain every migration difference/rejection; and execute provider/deployment checks. Record skipped or unavailable checks as gaps, never as passes. Complete the [go-live checklist](RIVERBEND_GO_LIVE_CHECKLIST.md); the current decision is **not ready for scenario go-live**.
