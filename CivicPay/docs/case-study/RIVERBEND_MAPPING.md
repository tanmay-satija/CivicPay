# Riverbend — source-to-target mapping

Fictional data and proposed transformation specification. These examples have not been imported as a Riverbend acceptance run. See [solution decisions](RIVERBEND_SOLUTION_DESIGN.md).

## Migration boundary

Choose a cutover timestamp T0, stop legacy writes, and export outstanding balances as of T0. The sample represents a snapshot at `2026-10-01T00:00:00Z`; payment examples are **subsequent** activity. Historical payments already included in the snapshot are excluded from balance-deducting imports. If history migration is later required, agree a pre-history opening balance instead of mixing both approaches.

Retain the source export, checksum, extraction timestamp, row counts and per-service balance totals in a migration manifest. File contents and these manifest fields are not stored by CivicPay; the implementation specialist maintains them separately. Quarantine unmapped services, non-CAD values, ambiguous dates and normalization collisions before upload.

## Account mapping

Illustrative legacy input:

```csv
serviceCode,legacyAccountId,openingBalance,currency
PT,0001,2000.00,CAD
UT,0001,400.00,CAD
PM,0001,250.00,CAD
```

| Source | CivicPay target | Transformation / validation |
|---|---|---|
| Municipal export context | `municipalityCode` | Constant `RIVERBEND`; configured before import |
| `serviceCode` | `paymentType` | `PT` → `PropertyTax`; `UT` → `Utility`; `PM` → `Permit`; unknown codes quarantined |
| Service + `legacyAccountId` | `accountNumber` | `RB-PT-0001`, `RB-UT-0001`, `RB-PER-0001`; preserve leading zeros, trim/uppercase; max 60 characters |
| `openingBalance` | `balance` | Nonnegative CAD decimal, at most two places, max 9,999,999,999.99; no rounding or thousands separators |
| `currency` | Pre-upload currency check | Require `CAD`; target account CSV has no currency column |
| Extract timestamp/checksum | External manifest | Retained for sign-off; not a target CSV field |

Account uniqueness is municipality + account number, not municipality + number + service. Service prefixes prevent the three legacy `0001` values from colliding. Maintain one stable crosswalk shared by CSV transformation and REST delivery.

Exact target account file, `rb-accounts.csv`:

```csv
municipalityCode,accountNumber,paymentType,balance
RIVERBEND,RB-PT-0001,PropertyTax,2000.00
RIVERBEND,RB-UT-0001,Utility,400.00
RIVERBEND,RB-PER-0001,Permit,250.00
```

Upload using multipart `POST /api/imports`, `kind=accounts`, `file=@rb-accounts.csv`. Expected on an empty scenario database: 3 imported, 0 rejected/skipped, CAD 2,650.00 source/imported, CAD 0 difference, `sourceAmountComplete=true`, `RECONCILED`. Zero-balance accounts are valid; negative/credit balances require a separate business disposition.

## Transaction mapping

| Legacy source | REST / payment CSV target | Rule |
|---|---|---|
| Municipal identifier | `municipalityCode` | `RIVERBEND`; 1–40 characters |
| Service + account ID | `accountNumber`, `paymentType` | Same crosswalk as the account file; account must exist and match service |
| Paid amount | `amount` | Positive CAD decimal; minimum and balance rules apply |
| Effective payment date | `transactionDate` | Convert an unambiguous source date to `yyyy-MM-dd`; validate against today UTC |
| Immutable legacy transaction ID | `externalReference` | Example `RB-LEGACY-20261002-000001`; max 100 characters; deterministic, trimmed, case preserved |
| Source currency | Adapter/transform check | CAD only; no target currency field and no currency conversion |

Reserve a documented reference namespace for each source system. References are unique per municipality and shared across REST and CSV submissions. Identical cross-channel retries deduplicate; a different amount/date/account/type under the same reference conflicts. Never regenerate a reference merely because delivery timed out.

Optional post-snapshot payment file, `rb-payments.csv`:

```csv
municipalityCode,accountNumber,paymentType,amount,transactionDate,externalReference
RIVERBEND,RB-PT-0001,PropertyTax,100.00,2026-10-02,RB-LEGACY-20261002-000001
RIVERBEND,RB-UT-0001,Utility,50.00,2026-10-02,RB-LEGACY-20261002-000002
RIVERBEND,RB-PER-0001,Permit,250.00,2026-10-02,RB-LEGACY-20261002-000003
```

Expected first import after accounts: 3 imported, CAD 400.00 source/imported, zero difference, `RECONCILED`. Remaining balances: Property Tax 1,900.00; Utility 350.00; Permit 0.00; total CAD 2,250.00. This valid full-Permit example does not prove partial-Permit rejection.

Equivalent REST request for the first payment:

```json
{
  "municipalityCode": "RIVERBEND",
  "accountNumber": "RB-PT-0001",
  "paymentType": "PropertyTax",
  "amount": 100.00,
  "transactionDate": "2026-10-02",
  "externalReference": "RB-LEGACY-20261002-000001"
}
```

Choose CSV **or** REST for first delivery. If CSV already accepted this reference, the REST request should replay rather than create another payment.

## Row handling and reconciliation

Use strict UTF-8 and the exact target headers/order; files are limited to 2,000,000 bytes and 5,000 data rows. Row numbers are logical CSV records: first data row is 2. A file-level format failure creates no batch; a 201 import response can still contain rejected rows.

Source amount − **newly imported** amount = difference. Keep account-opening totals separate from payment totals. Repeating the payment file should produce 3 skipped rows, CAD 0 newly imported, CAD 400.00 difference and `WARNING`. That is expected duplicate protection, not a second transfer of money. Unparseable amounts or missing row outcomes make source totals incomplete.

An identical account re-import skips only while type/balance still match. After payments reduce balances, the original opening file can yield `ACCOUNT_CONFLICT`; it never resets balances. Preserve corrected-file lineage and disposition every rejection instead of treating an upload success as migration sign-off. [Detailed migration contract](../DATA_MIGRATION_GUIDE.md).
