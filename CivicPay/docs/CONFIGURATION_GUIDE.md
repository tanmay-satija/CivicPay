# Municipality configuration

The catalogue is `PropertyTax`, `Utility`, `ParkingTicket`, `BusinessLicence`, `Permit`. Codes are case-sensitive catalogue identifiers; municipality and account codes normalize to trimmed uppercase. External references trim surrounding whitespace but retain case (binary, case-sensitive equality on both providers; see integration guide).

| Setting | Validation / effect |
|---|---|
| municipalityCode | 1–40 letters, digits or hyphens; unique; immutable route identity |
| municipalityName | Required, at most 120 characters |
| currency | CAD only in this synthetic implementation |
| minimumPayment | Positive decimal with at most two places; max 9,999,999,999.99 |
| allowPartialPayments | If false, amount must equal current outstanding balance |
| acceptedPaymentTypes | Nonempty, distinct list from catalogue |
| version | Required current GUID for updates; omitted for creation |

Payment amount cannot exceed account balance and must meet the minimum even when paying the final balance. Implementation staff should choose minimums compatible with legacy small balances; this project does not have a final-balance exception.

```json
{
  "municipalityCode": "DEMO-COUNTY",
  "municipalityName": "Demo County",
  "currency": "CAD",
  "allowPartialPayments": true,
  "minimumPayment": 5,
  "acceptedPaymentTypes": ["PropertyTax", "Utility", "Permit"]
}
```

POST `/api/municipalities` creates a client. GET `/api/municipalities/{code}` retrieves the current configuration and version. PUT the same shape with that version to update it. A stale version returns CONFIGURATION_CONFLICT; reload and review the change. Payments and new account imports also advance this token. Disabling a payment type with outstanding accounts returns ACTIVE_ACCOUNTS. GET `/api/municipalities/{code}/payment-types` exposes the enabled catalogue subset.

The dashboard Municipalities page supports creation and editing. Municipality configuration is independent of dashboard reporting dates/outcomes; only the municipality selector narrows the list.

## Seeded clients

| Client | Partial payments | Minimum | Types |
|---|---|---|---|
| Demo County | Yes | CAD 5 | PropertyTax, Utility, Permit |
| Pine Valley | No | CAD 10 | PropertyTax, ParkingTicket |
| Riverbend | Yes | CAD 25 | Utility, BusinessLicence, Permit |

With Database__Seed=true, initialization seeds 90 accounts and 168 transactions, plus 12 labelled synthetic error examples. Request counts and response times use actual HTTP executions; seed scenarios do not fabricate request telemetry. Transactions span the preceding 28 days, using current UTC time. Existing municipal data prevents reseeding. Account balances reflect accepted seed transactions.

## Environment configuration

| Variable | Purpose |
|---|---|
| Database__Provider | SqlServer (default) or Sqlite |
| ConnectionStrings__CivicPay | Required SQL Server connection string; SQLite has a local file default |
| Database__Initialize | true applies SQL Server migrations / creates SQLite schema |
| Database__Seed | true seeds fictional data if municipalities are empty |
| Security__ApiKey | Random key, at least 24 characters; required by default |
| Demo__OpenAccess | false by default; true permitted only in Development/Testing |
| ASPNETCORE_ENVIRONMENT | Development for explicit local demo; Production in Compose |
| ASPNETCORE_URLS | Local listening URL, e.g. http://127.0.0.1:5080 |
| AllowedHosts | Semicolon-separated host allowlist; default localhost;127.0.0.1 |

MultipleActiveResultSets must be disabled for SQL Server; startup rejects it because import recovery needs EF savepoints. SQLite API connections enable foreign keys even when a custom connection string omits that setting. The five required payment types are initialized independently of Database__Seed.

Run only one API initializer when applying migrations/seeding. Use environment variables or local .env, never committed credentials.
