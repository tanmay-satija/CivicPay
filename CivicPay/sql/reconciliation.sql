-- Compare input and newly imported amounts; rejected/unparseable amounts remain visible.
WITH BatchTotals AS (
 SELECT b.Id, b.Kind, b.Status, b.ExpectedRecordCount,
        COUNT(r.Id) AS ReportedRecords,
        SUM(CASE WHEN r.Status = 'Imported' THEN 1 ELSE 0 END) AS ImportedRecords,
        SUM(CASE WHEN r.Status = 'Rejected' THEN 1 ELSE 0 END) AS RejectedRecords,
        SUM(CASE WHEN r.Status = 'Skipped' THEN 1 ELSE 0 END) AS SkippedRecords,
        COALESCE(SUM(r.SourceAmount),0) AS SourceAmount,
        COALESCE(SUM(r.ImportedAmount),0) AS ImportedAmount,
        SUM(CASE WHEN r.Id IS NOT NULL AND r.SourceAmount IS NULL THEN 1 ELSE 0 END) AS UnparseableAmounts
 FROM ImportBatches b LEFT JOIN ImportRecords r ON b.Id = r.ImportBatchId
 GROUP BY b.Id, b.Kind, b.Status, b.ExpectedRecordCount
)
SELECT *, SourceAmount - ImportedAmount AS Difference,
 CASE WHEN Status <> 'Completed' OR (ReportedRecords > 0 AND RejectedRecords = ReportedRecords) THEN 'FAILED'
      WHEN UnparseableAmounts > 0 OR ReportedRecords <> ExpectedRecordCount OR RejectedRecords > 0 OR SkippedRecords > 0 OR SourceAmount <> ImportedAmount THEN 'WARNING'
      ELSE 'RECONCILED' END AS ReconciliationStatus
FROM BatchTotals;
