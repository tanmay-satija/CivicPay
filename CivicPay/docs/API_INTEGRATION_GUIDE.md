# API integration guide

OpenAPI JSON: `/swagger/v1/swagger.json`; interactive UI: `/swagger`. API uses JSON except multipart CSV upload. Swagger's Authorize control records the key; supply X-Api-Key in requests. Dashboard API access stores its key only in memory for the tab.

## Endpoint contract

| Method / path | Purpose | Successful response |
|---|---|---|
| GET /api/health | Database connectivity | 200; 503 when unavailable |
| GET /api/municipalities | Configurations | 200 array |
| GET /api/municipalities/{code} | Configuration and version | 200 object |
| POST /api/municipalities | Create configuration | 201 object + Location |
| PUT /api/municipalities/{code} | Update with current version | 200 object |
| GET /api/municipalities/{code}/payment-types | Enabled services | 200 array |
| POST /api/payments | Accept simulated record | 201 new / 200 identical retry |
| GET /api/payments/{id} | Accepted record | 200 object |
| POST /api/imports | Multipart file + kind | 201 reconciliation + Location |
| GET /api/imports/{id}/records | Row results, page/pageSize | 200 paginated object |
| GET /api/imports/{id}/reconciliation | Reconciliation | 200 object |
| GET /api/monitoring | Dashboard aggregate snapshot | 200 object |
| GET /api/monitoring/accounts | Accounts, municipality/page/pageSize | 200 paginated object |

Both paginated endpoints accept page>=1 and pageSize=1..200 (default 100), with an offset no larger than 2,147,483,647. Monitoring accepts municipality, from, to (yyyy-MM-dd), and status=Accepted/Rejected. Date bounds are inclusive UTC **record creation dates**, not the imported transaction's effective date. Outcome filters request status codes, accepted transactions and reconciliation success/warning/failure. Global municipality configurations remain visible regardless of outcome/date filters. A selected municipality matches batches containing that municipality; batch totals still cover the whole source file.

## Simulated payment

Import `data/valid/accounts.csv` first, then use a unique reference:

```bash
curl -i http://localhost:5080/api/payments \
  -H "X-Api-Key: $CIVICPAY_API_KEY" \
  -H 'Content-Type: application/json' \
  -d '{"municipalityCode":"DEMO-COUNTY","accountNumber":"LEGACY-PT-1001","paymentType":"PropertyTax","amount":25.00,"transactionDate":"2026-01-01","externalReference":"INTEGRATION-DEMO-001"}'
```

```json
{"success":true,"transactionId":"<generated-guid>","status":"Accepted","replayed":false}
```

Amount must be positive CAD cents, within decimal(12,2). Date is an ISO calendar date between 2000-01-01 and today UTC. Required identifiers must be nonblank. Account must belong to that municipality and payment type. An overpayment, too-small payment or disallowed partial payment is rejected without modifying the balance.

## Idempotency

Keep the same externalReference when retrying an uncertain request. The unique key is municipality + external reference. Normalized account, payment type, amount and date must be identical. An identical retry returns the original ID and replayed=true even if the account is now paid or rules changed. A changed payload returns DUPLICATE_REFERENCE (409). Different municipalities may use the same reference.

External references are trimmed and case-sensitive. The forward SQL Server migration applies Latin1_General_100_BIN2 to this column; SQLite uses its default binary equality. Keep retry references exactly stable. There is no separate Idempotency-Key header and no automatic outbound retry. The database unique index remains authoritative under races.

## Errors and retries

```json
{"success":false,"errorCode":"UNSUPPORTED_PAYMENT_TYPE","message":"This municipality does not accept this payment type.","correlationId":"<generated-id>"}
```

| HTTP | Common codes | Client action |
|---|---|---|
| 400 | INVALID_REQUEST, INVALID_CSV, INVALID_CSV_HEADER, INVALID_CSV_ENCODING, INVALID_DATE_RANGE, INVALID_STATUS, INVALID_PAGINATION, CODE_MISMATCH, INVALID_IMPORT_KIND, INVALID_FILE_SIZE | Correct format/parameters |
| 401 | UNAUTHORIZED | Supply valid X-Api-Key |
| 404 | MUNICIPALITY_NOT_FOUND, ACCOUNT_NOT_FOUND, BATCH_NOT_FOUND, TRANSACTION_NOT_FOUND, ENDPOINT_NOT_FOUND | Check mapping/initialization |
| 409 | DUPLICATE_REFERENCE, MUNICIPALITY_EXISTS, CONFIGURATION_CONFLICT, ACTIVE_ACCOUNTS, ACCOUNT_CONFLICT, DATABASE_CONFLICT | Resolve conflicting input; reload or retry concurrency conflicts |
| 405 | METHOD_NOT_ALLOWED | Use the documented HTTP method |
| 415 | UNSUPPORTED_MEDIA_TYPE | Use JSON or multipart as documented |
| 413 | IMPORT_TOO_LARGE | Split bounded files |
| 422 | REQUIRED_FIELDS, INVALID_AMOUNT, INVALID_DATE, BELOW_MINIMUM, OVERPAYMENT, PARTIAL_PAYMENT_NOT_ALLOWED, ACCOUNT_TYPE_MISMATCH, UNSUPPORTED_PAYMENT_TYPE, INVALID_CONFIGURATION | Correct client mapping/rules |
| 500 | INTERNAL_ERROR | Save correlation ID; troubleshoot; retry payment with same reference only |

X-Correlation-ID appears on every response. Safe errors do not expose stack traces. Schema/model-binding errors use INVALID_REQUEST. Empty form submissions are model-binding failures. Import row codes appear in the report rather than failing the whole validly formatted file. Unknown API routes and unsupported methods/content types also return the structured error envelope. Client cancellation does not create an internal-error record.

## Client implementation checklist

1. Retrieve enabled types and map legacy codes explicitly.
2. Migrate accounts before payments; verify balance semantics with fictional client stakeholders.
3. Store immutable external references and serialize amounts as decimals, dates as ISO dates.
4. Exercise a successful record, identical retry, changed-payload conflict and unsupported type.
5. Retain response correlation IDs for troubleshooting.
6. Reconcile imports and resolve rejected rows before approving the demo go-live.
