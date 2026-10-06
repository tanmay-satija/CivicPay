WITH Requests AS (
 SELECT MunicipalityCode, COUNT(*) AS RequestCount,
        SUM(CASE WHEN StatusCode < 400 THEN 1 ELSE 0 END) AS SuccessfulRequests,
        AVG(DurationMs) AS AverageResponseMs
 FROM IntegrationEvents WHERE CorrelationId NOT LIKE 'synthetic-%'
 GROUP BY MunicipalityCode
), Payments AS (
 SELECT MunicipalityId, COUNT(*) AS PaymentCount, SUM(Amount) AS RecordedAmount
 FROM Transactions GROUP BY MunicipalityId
)
SELECT m.Code, m.Name, COALESCE(r.RequestCount,0) AS Requests,
       COALESCE(CAST(100.0 * r.SuccessfulRequests / NULLIF(r.RequestCount,0) AS decimal(6,2)),0) AS SuccessRate,
       COALESCE(r.AverageResponseMs,0) AS AverageResponseMs, COALESCE(p.PaymentCount,0) AS AcceptedRecords,
       COALESCE(p.RecordedAmount,0) AS RecordedAmount,
       DENSE_RANK() OVER (ORDER BY COALESCE(p.RecordedAmount,0) DESC) AS AmountRank
FROM Municipalities m
LEFT JOIN Requests r ON r.MunicipalityCode = m.Code
LEFT JOIN Payments p ON p.MunicipalityId = m.Id
ORDER BY AmountRank, m.Code;
