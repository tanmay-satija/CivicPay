# Five-minute portfolio walkthrough

1. Open Overview and describe the three fictional clients and their different rules. Explain the data is synthetic.
2. Open Municipalities; show Pine Valley full-balance policy versus Demo County partial-payment support. Inspect ConfigurationService and Application/Rules.cs.
3. Submit a simulated payment in Transactions, then demonstrate an identical retry with curl using the same account/amount/date/reference. Explain the unique index and balance/version update in PaymentService.
4. Open Imports; upload valid accounts before valid payments. Show zero reconciliation difference. Upload invalid payments and inspect missing account, unsupported type, malformed amount/date and duplicate outcomes.
5. Open Integration Health and Error explorer. Filter a municipality; connect a correlation ID to an API response and structured logs.
6. Run tests. State which provider was actually exercised and that SQL Server/Docker verification is a separate gate. Show sql/municipality_performance.sql to explain joins, aggregates, CASE, CTE and DENSE_RANK.

Discussion prompts: Why does retry lookup happen before balance validation? Why is a unique index insufficient to protect balances? Why does a repeated import warn instead of claiming newly moved amounts? How are unknown source amounts represented? What would change for larger datasets or real authentication?

Use [VERIFICATION.md](VERIFICATION.md) as the evidence record. Do not describe this project as production payment software or real municipal implementation experience.
