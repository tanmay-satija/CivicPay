using CivicPay.Domain;
using Microsoft.EntityFrameworkCore;
namespace CivicPay.Infrastructure;

public class CivicPayDbContext(DbContextOptions<CivicPayDbContext> options) : DbContext(options)
{
    public DbSet<Municipality> Municipalities => Set<Municipality>();
    public DbSet<MunicipalityConfiguration> Configurations => Set<MunicipalityConfiguration>();
    public DbSet<PaymentType> PaymentTypes => Set<PaymentType>();
    public DbSet<MunicipalityPaymentType> MunicipalityPaymentTypes => Set<MunicipalityPaymentType>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<PaymentTransaction> Transactions => Set<PaymentTransaction>();
    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();
    public DbSet<ImportRecord> ImportRecords => Set<ImportRecord>();
    public DbSet<IntegrationEvent> IntegrationEvents => Set<IntegrationEvent>();
    public DbSet<ErrorLog> ErrorLogs => Set<ErrorLog>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Municipality>(e => { e.HasIndex(x => x.Code).IsUnique(); e.Property(x => x.Code).HasMaxLength(40); e.Property(x => x.Name).HasMaxLength(120); e.HasOne(x => x.Configuration).WithOne().HasForeignKey<MunicipalityConfiguration>(x => x.MunicipalityId); });
        b.Entity<MunicipalityConfiguration>(e => { e.HasKey(x => x.MunicipalityId); e.Property(x => x.Currency).HasMaxLength(3); e.Property(x => x.MinimumPayment).HasPrecision(12, 2); e.Property(x => x.Version).IsConcurrencyToken(); e.ToTable(t => t.HasCheckConstraint("CK_Configuration_Minimum", "MinimumPayment > 0")); });
        b.Entity<PaymentType>(e => { e.HasIndex(x => x.Code).IsUnique(); e.Property(x => x.Code).HasMaxLength(30); });
        b.Entity<MunicipalityPaymentType>().HasKey(x => new { x.MunicipalityId, x.PaymentTypeId });
        b.Entity<Municipality>().HasMany(x => x.PaymentTypes).WithOne().HasForeignKey(x => x.MunicipalityId);
        b.Entity<MunicipalityPaymentType>().HasOne(x => x.PaymentType).WithMany().OnDelete(DeleteBehavior.Restrict);
        b.Entity<Account>(e => { e.HasIndex(x => new { x.MunicipalityId, x.AccountNumber }).IsUnique(); e.HasAlternateKey(x => new { x.Id, x.MunicipalityId, x.PaymentTypeId }); e.Property(x => x.AccountNumber).HasMaxLength(60); e.Property(x => x.Balance).HasPrecision(12, 2); e.Property(x => x.Version).IsConcurrencyToken(); e.HasOne(x => x.Municipality).WithMany().OnDelete(DeleteBehavior.Restrict); e.HasOne(x => x.PaymentType).WithMany().OnDelete(DeleteBehavior.Restrict); e.ToTable(t => t.HasCheckConstraint("CK_Account_Balance", "Balance >= 0")); });
        b.Entity<PaymentTransaction>(e =>
        {
            e.HasIndex(x => new { x.MunicipalityId, x.ExternalReference }).IsUnique();
            e.HasIndex(x => new { x.MunicipalityId, x.TransactionDate });
            e.Property(x => x.ExternalReference).HasMaxLength(100);
            if (Database.IsSqlServer())
                e.Property(x => x.ExternalReference).UseCollation("Latin1_General_100_BIN2");
            e.Property(x => x.Amount).HasPrecision(12, 2);
            e.Property(x => x.Status).HasMaxLength(20);
            e.HasOne(x => x.Account).WithMany().HasForeignKey(x => new { x.AccountId, x.MunicipalityId, x.PaymentTypeId }).HasPrincipalKey(x => new { x.Id, x.MunicipalityId, x.PaymentTypeId }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Municipality).WithMany().OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.PaymentType).WithMany().OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ImportBatch>().WithMany().HasForeignKey(x => x.ImportBatchId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("CK_Transaction_Amount", "Amount > 0"));
        });
        b.Entity<ImportBatch>(e => { e.Property(x => x.FileName).HasMaxLength(120); e.Property(x => x.Kind).HasMaxLength(20); e.Property(x => x.Status).HasMaxLength(20); });
        b.Entity<ImportRecord>(e => { e.HasIndex(x => new { x.ImportBatchId, x.RowNumber }).IsUnique(); e.HasOne<ImportBatch>().WithMany(x => x.Records).HasForeignKey(x => x.ImportBatchId); e.HasOne<PaymentTransaction>().WithMany().HasForeignKey(x => x.TransactionId).OnDelete(DeleteBehavior.Restrict); e.Property(x => x.SourceAmount).HasPrecision(14, 2); e.Property(x => x.ImportedAmount).HasPrecision(14, 2); e.Property(x => x.MunicipalityCode).HasMaxLength(40); e.Property(x => x.Status).HasMaxLength(20); e.Property(x => x.ErrorCode).HasMaxLength(60); e.Property(x => x.Message).HasMaxLength(400); });
        b.Entity<IntegrationEvent>(e => { e.HasIndex(x => new { x.MunicipalityCode, x.CreatedAt }); e.Property(x => x.CorrelationId).HasMaxLength(80); e.Property(x => x.MunicipalityCode).HasMaxLength(40); e.Property(x => x.Path).HasMaxLength(200); e.Property(x => x.Method).HasMaxLength(10); e.Property(x => x.ErrorCode).HasMaxLength(60); });
        b.Entity<ErrorLog>(e => { e.HasIndex(x => x.CreatedAt); e.Property(x => x.CorrelationId).HasMaxLength(80); e.Property(x => x.MunicipalityCode).HasMaxLength(40); e.Property(x => x.ErrorCode).HasMaxLength(60); e.Property(x => x.Message).HasMaxLength(400); });
    }
}
