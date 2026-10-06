IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006001022_InitialSchema'
)
BEGIN
    CREATE TABLE [ErrorLogs] (
        [Id] bigint NOT NULL IDENTITY,
        [CorrelationId] nvarchar(80) NOT NULL,
        [MunicipalityCode] nvarchar(40) NULL,
        [ErrorCode] nvarchar(60) NOT NULL,
        [Message] nvarchar(400) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_ErrorLogs] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006001022_InitialSchema'
)
BEGIN
    CREATE TABLE [ImportBatches] (
        [Id] uniqueidentifier NOT NULL,
        [Kind] nvarchar(20) NOT NULL,
        [ExpectedRecordCount] int NOT NULL,
        [FileName] nvarchar(120) NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CompletedAt] datetimeoffset NULL,
        CONSTRAINT [PK_ImportBatches] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006001022_InitialSchema'
)
BEGIN
    CREATE TABLE [IntegrationEvents] (
        [Id] bigint NOT NULL IDENTITY,
        [CorrelationId] nvarchar(80) NOT NULL,
        [Path] nvarchar(200) NOT NULL,
        [Method] nvarchar(10) NOT NULL,
        [MunicipalityCode] nvarchar(40) NULL,
        [StatusCode] int NOT NULL,
        [DurationMs] float NOT NULL,
        [ErrorCode] nvarchar(60) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_IntegrationEvents] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006001022_InitialSchema'
)
BEGIN
    CREATE TABLE [Municipalities] (
        [Id] int NOT NULL IDENTITY,
        [Code] nvarchar(40) NOT NULL,
        [Name] nvarchar(120) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_Municipalities] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006001022_InitialSchema'
)
BEGIN
    CREATE TABLE [PaymentTypes] (
        [Id] int NOT NULL IDENTITY,
        [Code] nvarchar(30) NOT NULL,
        CONSTRAINT [PK_PaymentTypes] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006001022_InitialSchema'
)
BEGIN
    CREATE TABLE [Configurations] (
        [MunicipalityId] int NOT NULL,
        [Currency] nvarchar(3) NOT NULL,
        [AllowPartialPayments] bit NOT NULL,
        [MinimumPayment] decimal(12,2) NOT NULL,
        [Version] uniqueidentifier NOT NULL,
        [UpdatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_Configurations] PRIMARY KEY ([MunicipalityId]),
        CONSTRAINT [CK_Configuration_Minimum] CHECK (MinimumPayment > 0),
        CONSTRAINT [FK_Configurations_Municipalities_MunicipalityId] FOREIGN KEY ([MunicipalityId]) REFERENCES [Municipalities] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006001022_InitialSchema'
)
BEGIN
    CREATE TABLE [Accounts] (
        [Id] int NOT NULL IDENTITY,
        [MunicipalityId] int NOT NULL,
        [AccountNumber] nvarchar(60) NOT NULL,
        [PaymentTypeId] int NOT NULL,
        [Balance] decimal(12,2) NOT NULL,
        [Version] uniqueidentifier NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_Accounts] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Accounts_Id_MunicipalityId_PaymentTypeId] UNIQUE ([Id], [MunicipalityId], [PaymentTypeId]),
        CONSTRAINT [CK_Account_Balance] CHECK (Balance >= 0),
        CONSTRAINT [FK_Accounts_Municipalities_MunicipalityId] FOREIGN KEY ([MunicipalityId]) REFERENCES [Municipalities] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Accounts_PaymentTypes_PaymentTypeId] FOREIGN KEY ([PaymentTypeId]) REFERENCES [PaymentTypes] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006001022_InitialSchema'
)
BEGIN
    CREATE TABLE [MunicipalityPaymentTypes] (
        [MunicipalityId] int NOT NULL,
        [PaymentTypeId] int NOT NULL,
        CONSTRAINT [PK_MunicipalityPaymentTypes] PRIMARY KEY ([MunicipalityId], [PaymentTypeId]),
        CONSTRAINT [FK_MunicipalityPaymentTypes_Municipalities_MunicipalityId] FOREIGN KEY ([MunicipalityId]) REFERENCES [Municipalities] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_MunicipalityPaymentTypes_PaymentTypes_PaymentTypeId] FOREIGN KEY ([PaymentTypeId]) REFERENCES [PaymentTypes] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006001022_InitialSchema'
)
BEGIN
    CREATE TABLE [Transactions] (
        [Id] uniqueidentifier NOT NULL,
        [MunicipalityId] int NOT NULL,
        [AccountId] int NOT NULL,
        [PaymentTypeId] int NOT NULL,
        [Amount] decimal(12,2) NOT NULL,
        [TransactionDate] date NOT NULL,
        [ExternalReference] nvarchar(100) NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [ImportBatchId] uniqueidentifier NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_Transactions] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Transaction_Amount] CHECK (Amount > 0),
        CONSTRAINT [FK_Transactions_Accounts_AccountId_MunicipalityId_PaymentTypeId] FOREIGN KEY ([AccountId], [MunicipalityId], [PaymentTypeId]) REFERENCES [Accounts] ([Id], [MunicipalityId], [PaymentTypeId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Transactions_ImportBatches_ImportBatchId] FOREIGN KEY ([ImportBatchId]) REFERENCES [ImportBatches] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Transactions_Municipalities_MunicipalityId] FOREIGN KEY ([MunicipalityId]) REFERENCES [Municipalities] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Transactions_PaymentTypes_PaymentTypeId] FOREIGN KEY ([PaymentTypeId]) REFERENCES [PaymentTypes] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006001022_InitialSchema'
)
BEGIN
    CREATE TABLE [ImportRecords] (
        [Id] bigint NOT NULL IDENTITY,
        [ImportBatchId] uniqueidentifier NOT NULL,
        [RowNumber] int NOT NULL,
        [MunicipalityCode] nvarchar(40) NOT NULL,
        [SourceAmount] decimal(14,2) NULL,
        [ImportedAmount] decimal(14,2) NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [ErrorCode] nvarchar(60) NULL,
        [Message] nvarchar(400) NULL,
        [TransactionId] uniqueidentifier NULL,
        CONSTRAINT [PK_ImportRecords] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ImportRecords_ImportBatches_ImportBatchId] FOREIGN KEY ([ImportBatchId]) REFERENCES [ImportBatches] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_ImportRecords_Transactions_TransactionId] FOREIGN KEY ([TransactionId]) REFERENCES [Transactions] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006001022_InitialSchema'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Accounts_MunicipalityId_AccountNumber] ON [Accounts] ([MunicipalityId], [AccountNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006001022_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_Accounts_PaymentTypeId] ON [Accounts] ([PaymentTypeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006001022_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_ErrorLogs_CreatedAt] ON [ErrorLogs] ([CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006001022_InitialSchema'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ImportRecords_ImportBatchId_RowNumber] ON [ImportRecords] ([ImportBatchId], [RowNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006001022_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_ImportRecords_TransactionId] ON [ImportRecords] ([TransactionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006001022_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_IntegrationEvents_MunicipalityCode_CreatedAt] ON [IntegrationEvents] ([MunicipalityCode], [CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006001022_InitialSchema'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Municipalities_Code] ON [Municipalities] ([Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006001022_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_MunicipalityPaymentTypes_PaymentTypeId] ON [MunicipalityPaymentTypes] ([PaymentTypeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006001022_InitialSchema'
)
BEGIN
    CREATE UNIQUE INDEX [IX_PaymentTypes_Code] ON [PaymentTypes] ([Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006001022_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_Transactions_AccountId_MunicipalityId_PaymentTypeId] ON [Transactions] ([AccountId], [MunicipalityId], [PaymentTypeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006001022_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_Transactions_ImportBatchId] ON [Transactions] ([ImportBatchId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006001022_InitialSchema'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Transactions_MunicipalityId_ExternalReference] ON [Transactions] ([MunicipalityId], [ExternalReference]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006001022_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_Transactions_MunicipalityId_TransactionDate] ON [Transactions] ([MunicipalityId], [TransactionDate]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006001022_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_Transactions_PaymentTypeId] ON [Transactions] ([PaymentTypeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006001022_InitialSchema'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261006001022_InitialSchema', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006003130_CatalogueAndReferenceEquality'
)
BEGIN
    INSERT INTO PaymentTypes (Code)
    SELECT catalogue.Code
    FROM (VALUES ('PropertyTax'), ('Utility'), ('ParkingTicket'), ('BusinessLicence'), ('Permit')) AS catalogue(Code)
    WHERE NOT EXISTS (SELECT 1 FROM PaymentTypes existing WHERE existing.Code = catalogue.Code);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006003130_CatalogueAndReferenceEquality'
)
BEGIN
    DROP INDEX [IX_Transactions_MunicipalityId_ExternalReference] ON [Transactions];
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Transactions]') AND [c].[name] = N'ExternalReference');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [Transactions] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [Transactions] ALTER COLUMN [ExternalReference] nvarchar(100) COLLATE Latin1_General_100_BIN2 NOT NULL;
    CREATE UNIQUE INDEX [IX_Transactions_MunicipalityId_ExternalReference] ON [Transactions] ([MunicipalityId], [ExternalReference]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006003130_CatalogueAndReferenceEquality'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261006003130_CatalogueAndReferenceEquality', N'10.0.10');
END;

COMMIT;
GO
