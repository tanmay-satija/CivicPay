using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicPay.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CatalogueAndReferenceEquality : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO PaymentTypes (Code)
                SELECT catalogue.Code
                FROM (VALUES ('PropertyTax'), ('Utility'), ('ParkingTicket'), ('BusinessLicence'), ('Permit')) AS catalogue(Code)
                WHERE NOT EXISTS (SELECT 1 FROM PaymentTypes existing WHERE existing.Code = catalogue.Code);
                """);
            migrationBuilder.AlterColumn<string>(
                name: "ExternalReference",
                table: "Transactions",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                collation: "Latin1_General_100_BIN2",
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Preserve reference data: existing configurations/accounts may depend on it.
            // Reverting collation can fail if references now differ only by case.
            migrationBuilder.AlterColumn<string>(
                name: "ExternalReference",
                table: "Transactions",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100,
                oldCollation: "Latin1_General_100_BIN2");
        }
    }
}
