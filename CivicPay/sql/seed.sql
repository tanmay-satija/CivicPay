-- Alternative SQL-only seed for an EMPTY, already-migrated CivicPay database.
-- Use Database__Seed=true for the richer application-generated dataset instead.
-- Do not combine the two seed paths. Never run against real municipal data.
SET XACT_ABORT ON;
BEGIN TRY
BEGIN TRANSACTION;
IF EXISTS (SELECT 1 FROM Municipalities) THROW 50001, 'SQL seed requires an empty municipality table.', 1;
INSERT INTO PaymentTypes(Code)
SELECT catalogue.Code FROM (VALUES ('PropertyTax'),('Utility'),('ParkingTicket'),('BusinessLicence'),('Permit')) AS catalogue(Code)
WHERE NOT EXISTS (SELECT 1 FROM PaymentTypes existing WHERE existing.Code=catalogue.Code);
INSERT INTO Municipalities(Code,Name,CreatedAt)
VALUES ('DEMO-COUNTY','Demo County',SYSDATETIMEOFFSET()),('PINE-VALLEY','Pine Valley',SYSDATETIMEOFFSET()),('RIVERBEND','Riverbend',SYSDATETIMEOFFSET());
INSERT INTO Configurations(MunicipalityId,Currency,AllowPartialPayments,MinimumPayment,Version,UpdatedAt)
SELECT Id,'CAD',CASE WHEN Code='PINE-VALLEY' THEN 0 ELSE 1 END,
 CASE Code WHEN 'DEMO-COUNTY' THEN 5 WHEN 'PINE-VALLEY' THEN 10 ELSE 25 END,NEWID(),SYSDATETIMEOFFSET()
FROM Municipalities;
INSERT INTO MunicipalityPaymentTypes(MunicipalityId,PaymentTypeId)
SELECT m.Id,p.Id FROM Municipalities m CROSS JOIN PaymentTypes p
WHERE (m.Code='DEMO-COUNTY' AND p.Code IN ('PropertyTax','Utility','Permit'))
   OR (m.Code='PINE-VALLEY' AND p.Code IN ('PropertyTax','ParkingTicket'))
   OR (m.Code='RIVERBEND' AND p.Code IN ('Utility','BusinessLicence','Permit'));
WITH Numbers AS (SELECT 1 AS n UNION ALL SELECT n+1 FROM Numbers WHERE n<10)
INSERT INTO Accounts(MunicipalityId,AccountNumber,PaymentTypeId,Balance,Version,CreatedAt)
SELECT mp.MunicipalityId,UPPER(p.Code)+'-'+RIGHT('0000'+CAST(n AS varchar(4)),4),p.Id,1000,NEWID(),SYSDATETIMEOFFSET()
FROM MunicipalityPaymentTypes mp JOIN PaymentTypes p ON p.Id=mp.PaymentTypeId CROSS JOIN Numbers;
-- Record a synthetic partial payment on Demo County accounts, and update balances atomically.
INSERT INTO Transactions(Id,MunicipalityId,AccountId,PaymentTypeId,Amount,TransactionDate,ExternalReference,Status,CreatedAt)
SELECT NEWID(),a.MunicipalityId,a.Id,a.PaymentTypeId,25,CAST(SYSUTCDATETIME() AS date),
 'SQL-SEED-'+CAST(a.Id AS varchar(12)),'Accepted',SYSDATETIMEOFFSET()
FROM Accounts a JOIN Municipalities m ON m.Id=a.MunicipalityId WHERE m.Code='DEMO-COUNTY';
UPDATE a SET Balance=Balance-25,Version=NEWID()
FROM Accounts a JOIN Municipalities m ON m.Id=a.MunicipalityId WHERE m.Code='DEMO-COUNTY';
COMMIT;
END TRY
BEGIN CATCH
 IF @@TRANCOUNT > 0 ROLLBACK;
 THROW;
END CATCH;
