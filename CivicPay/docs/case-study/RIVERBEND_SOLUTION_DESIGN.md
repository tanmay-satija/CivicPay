# Riverbend — solution design

Fictional exercise; proposed configuration and integration design. See [requirements and status](RIVERBEND_REQUIREMENTS.md). No Catalis architecture or process is represented.

## Configuration decisions

| Setting | Proposed decision | Rationale / current support |
|---|---|---|
| Municipality | `RIVERBEND`, display name `Riverbend` | One municipal identity across all services |
| Services | `PropertyTax`, `Utility`, `Permit` | Explicit legacy-code mapping; no Business Licence service in this scenario |
| Currency | `CAD` | Only supported configuration currency |
| Minimum | `25.00` | Assumed common minimum; revenue approval pending |
| Property Tax / Utility | Partial payments allowed | Supported by the municipal partial-payment flag; Utility policy is an assumption |
| Permits | Full outstanding balance required | **Not expressible independently with current configuration** |

### Gap G-01: service-specific partial-payment policy

`allowPartialPayments` belongs to the municipality, not a payment type. Setting it to `true` allows Permit partial payments; setting it to `false` also blocks Property Tax partial payments. Neither value meets both requirements.

The design requires a future, separately scoped change to enforce per-service policy in the shared payment validation path, covering REST and payment imports. An external adapter check alone is insufficient because other API callers and CSV uploads can bypass it. Do not split Riverbend into artificial municipalities to conceal this gap. **Go-live remains blocked until RB-03 and RB-04 pass together.** No such feature is implemented by this documentation.

## Supported prototype configuration

Use a fresh isolated database with `Database__Seed=false`. The existing seeded Riverbend accepts Utility, BusinessLicence and Permit; it is not this scenario. Disabling a seeded service with outstanding accounts can return `ACTIVE_ACCOUNTS`.

For the isolated prototype, `POST /api/municipalities` accepts:

```json
{
  "municipalityCode": "RIVERBEND",
  "municipalityName": "Riverbend",
  "currency": "CAD",
  "allowPartialPayments": true,
  "minimumPayment": 25.00,
  "acceptedPaymentTypes": ["PropertyTax", "Utility", "Permit"]
}
```

This is a **partial prototype**, not an approved Permit policy. Verify with `GET /api/municipalities/RIVERBEND` and `/payment-types`. If updating an existing scenario configuration, use `PUT /api/municipalities/RIVERBEND` with its current `version`. Payments and account imports also advance that token; reload after a `CONFIGURATION_CONFLICT` and review before retrying.

## Integration flow

1. Freeze/map the legacy account export and import accounts before enabling transaction delivery.
2. The **proposed municipal adapter** validates source currency, maps service/account codes, and records an immutable external reference and payload before submitting. No adapter implementation exists in this repository.
3. Submit JSON to `POST /api/payments` with `X-Api-Key`. CAD is implicit in municipal configuration; the payment DTO has no currency field. Adding a `currency` field is not a reliable currency-validation test.
4. CivicPay checks request shape, municipal reference replay, enabled service, account ownership/type, minimum and balance. It commits the accepted record and balance deduction together.
5. Store the returned transaction ID and `X-Correlation-ID`. A new record returns 201; an identical replay returns 200 with `replayed=true`. Read back with `GET /api/payments/{id}`.
6. For a timeout or uncertain 5xx, the proposed adapter retries the **same payload and reference** with bounded backoff. Correct mapping/validation failures instead of repeatedly submitting them; investigate 409 conflicts. CivicPay does not deliver outbound callbacks or implement this client retry queue.

## Validation and support

Positive amounts must have at most two decimal places and fit decimal(12,2). Payments must meet the minimum and not exceed the current balance. Dates must be between `2000-01-01` and today UTC. Municipality/account identifiers normalize to trimmed uppercase; canonical service codes and external-reference case must be preserved. See [mapping](RIVERBEND_MAPPING.md) for exact lengths and examples.

Investigate safe error codes, municipality, timestamp, correlation ID and batch row outcomes. Consult authorized logs for the endpoint; the monitoring error DTO does not expose it. Never put credentials or stack traces in client tickets. Dashboard error history includes deliberately rejected test requests and is not a resolved/unresolved incident queue.

## Known limitations and release implications

- The Permit policy gap is a release blocker; service-specific minimums are also unsupported if later requested.
- Imports commit per row, not as one atomic file. Interrupted batches may retain committed rows or remain `Processing`; inspect records before rerunning.
- Accounts cannot be overwritten by import; no payment refund, void or reversal endpoint exists.
- One shared API key provides no user roles or municipality-scoped authorization. Restrict this exercise to a controlled synthetic sandbox.
- Dashboard lists are bounded snapshots, not complete migration evidence; use paginated row reports and source manifests.
- Existing verification used SQLite. SQL Server migration/runtime and Docker execution remain unverified; assess the selected deployment path before approval.

Implementation references: [configuration service](../../src/CivicPay.Infrastructure/ConfigurationService.cs), [payment service](../../src/CivicPay.Infrastructure/PaymentService.cs), [validation rules](../../src/CivicPay.Application/Rules.cs), [API guide](../API_INTEGRATION_GUIDE.md).
