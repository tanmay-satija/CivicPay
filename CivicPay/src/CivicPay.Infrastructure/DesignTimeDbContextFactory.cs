using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
namespace CivicPay.Infrastructure;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<CivicPayDbContext>
{
    public CivicPayDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__CivicPay") ?? "Server=localhost;Database=CivicPay;Integrated Security=True;Encrypt=True";
        return new(new DbContextOptionsBuilder<CivicPayDbContext>().UseSqlServer(connection).Options);
    }
}
