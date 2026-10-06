-- SQL parameters keep filters separate from query text.
DECLARE @FromDate date = '2026-01-01';
DECLARE @ToDate date = '2026-12-31';
SELECT m.Code, m.Name, pt.Code AS PaymentType,
       COUNT(*) AS AcceptedRecords, SUM(p.Amount) AS RecordedAmount,
       AVG(p.Amount) AS AverageAmount
FROM Transactions p
JOIN Municipalities m ON m.Id = p.MunicipalityId
JOIN PaymentTypes pt ON pt.Id = p.PaymentTypeId
WHERE p.TransactionDate BETWEEN @FromDate AND @ToDate
GROUP BY m.Code, m.Name, pt.Code
ORDER BY m.Code, RecordedAmount DESC;
