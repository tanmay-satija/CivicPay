# Sample implementation checklist

Client: __________  Municipality code: __________  Owner: __________  Date (UTC): __________

## Discovery and setup
- [ ] Fictional dataset confirmed; no banking/card data
- [ ] Canonical service mappings recorded
- [ ] Minimum, partial-payment policy and balance semantics reviewed
- [ ] .NET, Docker and database prerequisites checked
- [ ] Local credentials generated and kept outside Git
- [ ] SQL Server connectivity and migrations verified, or gap recorded
- [ ] Municipality configuration retrieved and reviewed

## Migration and integration
- [ ] Opening-balance account file imported before payments
- [ ] All account rows inspected; batch ID: __________
- [ ] Valid payment file reconciled; batch ID: __________
- [ ] Invalid file rejected/skipped rows inspected; batch ID: __________
- [ ] Unknown/negative/unparseable values corrected or documented
- [ ] New REST payment accepted
- [ ] Identical retry returns original transaction ID
- [ ] Changed payload under same reference rejected
- [ ] Unsupported service and account mismatch diagnosed
- [ ] Pine Valley partial-payment rejection demonstrated
- [ ] Malformed JSON returns safe error with correlation ID

## Review and handover
- [ ] Automated tests run; skipped SQL tests explicitly recorded
- [ ] HTTP smoke checks run against chosen database provider
- [ ] Dashboard six views, filters and API key access exercised
- [ ] Swagger requests/responses reviewed
- [ ] Docker image/Compose run verified, or gap recorded
- [ ] Reconciliation differences understood
- [ ] Synthetic demo screenshots captured
- [ ] Documentation reviewed by another developer
- [ ] Open issues / limitations: __________
- [ ] Portfolio demonstration approved by: __________
