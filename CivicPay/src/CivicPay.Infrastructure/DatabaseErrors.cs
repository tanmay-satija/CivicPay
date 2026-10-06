using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CivicPay.Infrastructure;

internal static class DatabaseErrors
{
    public static bool IsUniqueViolation(DbUpdateException exception) => exception.InnerException switch
    {
        SqlException sql => sql.Number is 2601 or 2627,
        SqliteException sqlite => sqlite.SqliteExtendedErrorCode is 1555 or 2067,
        _ => false
    };

    public static bool IsConcurrencyConflict(DbUpdateException exception) =>
        exception is DbUpdateConcurrencyException || IsUniqueViolation(exception);
}
