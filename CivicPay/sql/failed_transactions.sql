-- These are failed payment REQUESTS; no rejected transaction is inserted.
SELECT e.CreatedAt, e.MunicipalityCode, e.StatusCode, e.ErrorCode,
       e.CorrelationId, l.Message, e.DurationMs
FROM IntegrationEvents e
LEFT JOIN ErrorLogs l ON l.CorrelationId = e.CorrelationId
WHERE e.CorrelationId NOT LIKE 'synthetic-%' AND e.Path = '/api/payments' AND e.Method = 'POST' AND e.StatusCode >= 400
ORDER BY e.CreatedAt DESC;
