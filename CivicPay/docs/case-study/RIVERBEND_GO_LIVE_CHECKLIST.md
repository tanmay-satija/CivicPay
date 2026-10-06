# Riverbend — deployment and go-live checklist

Fictional release gate for a controlled synthetic implementation, not approval to process real payments. No Catalis procedures or internal architecture are claimed. **Decision: blocked by G-01; Riverbend UAT, migration and approvals pending.** Unchecked items below are intentionally incomplete.

## Requirements and configuration — revenue lead / implementation specialist

- [ ] Approve [requirements](RIVERBEND_REQUIREMENTS.md), CAD 25.00 minimum, Utility partial-payment assumption and disposition of balances below the minimum.
- [ ] Resolve G-01: full Permit payments enforced through REST and CSV while Property Tax partial payments remain allowed; retain T03/T04 evidence.
- [ ] Approve the service/account crosswalk, source currency check and T0 opening-balance interpretation.
- [ ] Retrieve and record final `RIVERBEND` settings and version; exactly PropertyTax, Utility, Permit enabled. Keep scenario data separate from seeded dashboard data.

## Deployment rehearsal — platform operator

- [ ] Build/test the reviewed revision; record actual failures, skips and provider. Existing SQLite results do not prove SQL Server execution.
- [ ] Use a dedicated synthetic database; `Database__Seed=false`, `Demo__OpenAccess=false`, `ASPNETCORE_ENVIRONMENT=Production`, and a generated `Security__ApiKey` of at least 24 characters kept outside Git/screenshots.
- [ ] Review committed SQL Server migrations and apply **one** initialization path under one owner after backup. Disable startup initialization for routine runs once schema preparation is complete; verify catalogue/configuration reads.
- [ ] Execute SQL Server connectivity/migration/constraint checks if SQL Server is chosen. Execute image/Compose startup and persistence checks if Docker is chosen; neither has prior executed verification in this project.
- [ ] Restrict hosts/network access and test the intended TLS boundary. The supplied Compose file binds loopback, uses a demo `sa` connection and trusts the server certificate; it is not a production deployment template. Provision reviewed credentials/certificate settings for the rehearsal target.
- [ ] Prove backup restore in a disposable environment; record recovery owner, estimated recovery time and the exact revision/configuration restored. Never test destructive recovery against the running portfolio database.
- [ ] Verify secured health (database connectivity), Swagger, dashboard API access and safe correlation-based logging. Application health alone does not certify municipal integration readiness.

## Migration and validation — implementation specialist / revenue reviewer

- [ ] Freeze legacy writes and pause municipal delivery at T0; preserve export/checksum, crosswalk and per-service source control totals.
- [ ] Import opening accounts before payments. Inspect paginated row outcomes; verify counts, complete source amounts and CAD 2,650.00 sample account reconciliation.
- [ ] Migrate only agreed post-snapshot payment activity. For the sample, verify CAD 400.00 payment reconciliation and remaining balances 1,900.00 / 350.00 / 0.00.
- [ ] Sign off each rejected/skipped row and corrected-file lineage. A WARNING needs an explained disposition; FAILED/Processing batches require investigation. HTTP 201 or a zero difference alone is insufficient evidence.
- [ ] Retain batch IDs, transaction references, before/after balances and reviewer sign-off. Do not combine account opening balances and payment amounts into one control total.
- [ ] Complete [T01–T13](RIVERBEND_TEST_PLAN.md), including duplicate/race checks, currency handling, security and both Permit channels. Keep planned outcomes separate from executed results.

## Cutover, recovery and handover — integrator / release owner

- [ ] Approve an explicit cutover time, writer ownership and rollback trigger, such as an unexplained balance difference, missing source rows or a Permit partial accepted. Keep a single authoritative writer throughout the switch.
- [ ] Before new CivicPay writes, rollback can restore the approved snapshot and keep delivery paused. After new writes, preserve accepted references and balances and agree recovery/reconciliation before restoring or switching writers; a blind restore would lose accepted activity. There is no automatic import rollback or payment reversal API.
- [ ] Enable the proposed adapter only after approval; verify new delivery, exact retry and readback in the synthetic release environment. Confirm source currency validation and retry persistence are actually implemented.
- [ ] Review early transaction failures, per-client request metrics and batch reports against expected outcomes. Deliberate negative-test errors may remain in dashboard history; do not require an artificial zero-error counter.
- [ ] Hand over configuration, mapping, source manifests, test evidence, batch/reference IDs, support ownership, known limitations and correlation-based troubleshooting instructions. Keep secrets and raw stack traces out of the handover.

## Decision record

| Item | Recorded state |
|---|---|
| Requirement / mapping sign-off | Pending |
| G-01 Permit policy | Open — blocking |
| Riverbend UAT / migration | Planned — no executed results |
| Deployment / recovery evidence | Pending; prior SQL Server / Docker gaps remain |
| Fictional revenue approver | Unassigned |
| Fictional technical / release approver | Unassigned |
| Approved cutover time / revision / evidence links | Not approved |

This document records gates and responsibilities; it does not imply a real municipal launch or that CivicPay is ready for production financial processing. [Implementation guide](../IMPLEMENTATION_GUIDE.md) · [Troubleshooting](../TROUBLESHOOTING.md).
