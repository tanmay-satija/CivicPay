# Legacy data migration

The CSV workflow is parse → validate → normalize → import → persisted row outcomes → reconciliation. The demo imports **account opening balances** first, then payment deductions. Opening balances must be pre-payment balances; importing already-netted balances would double-deduct and is a mapping mistake.

## Files and order

```bash
curl http://localhost:5080/api/imports -H "X-Api-Key: $CIVICPAY_API_KEY" \
 -F kind=accounts -F file=@data/valid/accounts.csv
curl http://localhost:5080/api/imports -H "X-Api-Key: $CIVICPAY_API_KEY" \
 -F kind=payments -F file=@data/valid/payments.csv
curl http://localhost:5080/api/imports -H "X-Api-Key: $CIVICPAY_API_KEY" \
 -F kind=payments -F file=@data/invalid/payments.csv
```

Exact headers and column order:

```csv
municipalityCode,accountNumber,paymentType,balance
municipalityCode,accountNumber,paymentType,amount,transactionDate,externalReference
```

UTF-8 (optional BOM), CRLF/LF, quoted commas, doubled quotes and quoted multiline fields are supported. Row numbers count logical CSV records including the header (first data record is 2), not physical lines. Do not include thousands separators, currency symbols or blank spacer rows. Identifiers are trimmed where documented; dates must be exactly yyyy-MM-dd. One source file can contain multiple municipalities.

Limits: 2,000,000 bytes of strict UTF-8, 5000 data rows; the multipart envelope is limited to 2,100,000 bytes. Oversize files/requests return 413. Invalid UTF-8 returns INVALID_CSV_ENCODING (400) before creating a batch. Unknown headers, malformed quoting or invalid kind produce a file-level 400 and no batch. Syntactically valid rows with wrong field counts become rejected records. Empty data with a valid header produces a zero-row reconciled batch.

## Outcomes

- Imported: validated and committed; amount contributes to newly imported total.
- Rejected: failed validation; code and safe message are persisted. No account balance/payment mutation occurs.
- Skipped: exact duplicate payment or identical existing account; zero newly imported amount. No silent discarding.

Payment rows reuse exactly the same service/rules as REST integrations. Identical payment retry is skipped; a changed payload under the same reference is rejected. Account import never overwrites an existing account. Identical type/balance skips; changed balance/type rejects ACCOUNT_CONFLICT. After payments change balances, rerunning the original opening-balance file may therefore reject those accounts. This prevents accidental resets.

Each row's business change and report commit together. Unexpected database/server failure marks the batch Failed when possible; previously committed rows survive. Expected record count remains visible if processing stops early. A sudden termination can leave Processing; inspect records and existing references before rerunning. No resumable/background importer is implemented. File contents are not retained; preserve the original synthetic source externally for audit.

## Reconciliation semantics

Response includes source/imported/rejected/skipped counts; sum of parseable source amounts; newly imported amount; source minus imported difference; sourceAmountComplete; status. Negative parseable source values remain in source totals to expose input quality. Malformed/missing amounts make sourceAmountComplete=false, so the sum is explicitly partial. Skipped duplicates remain in the source count/amount, yielding a warning rather than claiming a repeat import moved new money.

- RECONCILED: completed; all amounts known; no rejects/skips; difference zero.
- WARNING: completed with rejected/skipped rows, incomplete amounts, missing row reports or amount differences.
- FAILED: processing/failed infrastructure state, or all reported rows rejected.

For an account batch, amount means opening balance. For a payment batch, amount means simulated payment. Never combine these as one ledger total.

On a fresh seeded database, valid accounts import: 3 imported, source/imported CAD 1575. Valid payments import: 3 imported, source/imported CAD 250.50, difference zero. Repeating payments: 3 skipped, newly imported zero, difference CAD 250.50, WARNING. Invalid payment fixture after the valid import: 7 rejected, 1 skipped, incomplete amount total, WARNING because one row is skipped rather than rejected. Inspect individual errors to distinguish DUPLICATE_REFERENCE from DUPLICATE_REPLAY.

Use GET `/api/imports/{id}/records?page=1&pageSize=200` and `/reconciliation`. The Imports dashboard allows file upload and row inspection. Record fixture results and corrected-file references in the implementation checklist.
