# Riverbend — requirements and traceability

**Fictional implementation exercise.** Riverbend, its legacy system, roles and data are invented. This does not describe Catalis processes or internal architecture. Status: draft design; Riverbend acceptance testing and go-live approval have not occurred.

## Business context

Riverbend's revenue team wants to replace a legacy payment-record system while preserving account balances and integrating its municipal software. CivicPay records simulated payments; bank processing, settlement, refunds and real taxpayer information are outside this exercise.

The implementation lifecycle is **Requirements → Configuration → Integration → Migration → Testing → Validation → Go-Live**. Accounts must exist before payment integration tests or cutover transactions are submitted.

## Business requirements

| ID | Requirement / acceptance criterion |
|---|---|
| RB-01 | Enable Property Tax, Utility and Permit payments under one `RIVERBEND` municipality. |
| RB-02 | CAD only; quarantine non-CAD source records rather than silently relabelling them. |
| RB-03 | Allow Property Tax partial payments, subject to minimum and outstanding balance. |
| RB-04 | Require every Permit payment to equal the current outstanding balance. |
| RB-05 | Apply a proposed CAD 25.00 minimum to all three services; reject overpayments. |
| RB-06 | Migrate existing accounts from CSV with complete row and opening-balance reconciliation. |
| RB-07 | Accept transactions from external municipal software through REST, using mapped account/service identifiers. |
| RB-08 | Retry uncertain transactions without duplicate records or repeated balance deductions. |
| RB-09 | Account for every import row and explain rejected/skipped rows and amount differences. |
| RB-10 | Provide traceable, safe diagnostics and a documented release/handover decision. |

## Assumptions requiring fictional client sign-off

- Utility partial payments are allowed; the scenario did not specify this policy.
- CAD 25.00 is a proposed minimum, not a supplied business requirement. There is no final-balance exception: a balance below CAD 25.00 cannot be paid through the current payment rules. The revenue lead must resolve small balances before cutover or request a separate policy change.
- One account represents one service. The legacy export preserves leading zeros and uses a service-qualified identifier where numbers overlap.
- Account balances are taken at a single frozen cutover timestamp. Historical payments already reflected in those balances are not deducted again.

## Technical requirements

Use the existing JSON REST contract, `X-Api-Key`, decimal amounts with at most two places, ISO dates and stable external references. Import strict UTF-8 CSV with exact headers, at most 5,000 data rows and 2,000,000 file bytes. Preserve batch IDs, row outcomes, reconciliation totals and response correlation IDs. Keep credentials outside Git and use an isolated scenario database with synthetic seeding disabled.

The fictional revenue lead owns business acceptance, the implementation specialist owns mapping/migration evidence, the municipal software integrator owns request delivery, and the platform operator owns deployment/recovery checks. These are proposed responsibilities, not actual approvals.

## Requirements traceability matrix

“Supported” means present in current code; it does **not** mean Riverbend UAT passed. Every test ID below refers to the [test plan](RIVERBEND_TEST_PLAN.md).

| Requirement | Implementation | Test | Status |
|---|---|---|---|
| RB-01 Services | `acceptedPaymentTypes`; proposed configuration | T01 | Supported; scenario setup pending |
| RB-02 CAD | Configuration enforces CAD; proposed source currency check | T02 | Server support present; source check pending |
| RB-03 Property Tax partials | Municipal `allowPartialPayments=true` | T03 | Supported; Riverbend UAT pending |
| RB-04 Permit full payment | Requires service-specific enforcement on REST **and** CSV | T04 | **Gap G-01; go-live blocked** |
| RB-05 Minimum / balance | `Rules.PaymentBalance`; proposed CAD 25.00 | T05 | Supported; policy sign-off / UAT pending |
| RB-06 Account migration | `ImportService`, `kind=accounts`; proposed mapping | T06, T07 | Supported; Riverbend migration pending |
| RB-07 REST integration | `POST /api/payments`; proposed municipal adapter | T08 | Endpoint present; adapter / UAT pending |
| RB-08 Retry safety | Municipal reference uniqueness and atomic balance update | T09 | Baseline tested; Riverbend UAT pending |
| RB-09 Reconciliation | Row reports and source-minus-newly-imported totals | T10, T11 | Baseline tested; Riverbend validation pending |
| RB-10 Diagnostics / release | Safe errors, correlation IDs; proposed handover gates | T12, T13 | Baseline tested; deployment approval pending |

Current baseline evidence is in [UI/runtime verification](../UI_UX_VERIFICATION.md). No Riverbend results were generated for this documentation change.

Continue with [solution design](RIVERBEND_SOLUTION_DESIGN.md), [source mapping](RIVERBEND_MAPPING.md), [test plan](RIVERBEND_TEST_PLAN.md) and [go-live checklist](RIVERBEND_GO_LIVE_CHECKLIST.md).
