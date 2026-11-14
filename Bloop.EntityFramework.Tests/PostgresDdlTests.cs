using Bloop.EntityFramework.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Bloop.EntityFramework.Tests;

// Runs only when BLOOP_TEST_PG holds a connection string to a disposable PostgreSQL database.
public class PostgresDdlTests
{
    private static readonly string? ConnectionString = Environment.GetEnvironmentVariable("BLOOP_TEST_PG");

    private sealed class ShopInitializer : IDatabaseInitializer
    {
        public string Schema => "shop";

        public IReadOnlyList<DatabaseMigration> Migrations =>
        [
            new("001_tables", db => db.CreateTablesFromModelAsync([typeof(OrderLine), typeof(Order)])),
        ];
    }

    [Fact]
    public async Task Migration_CreatesTablesWithCrossSchemaForeignKey()
    {
        if (ConnectionString == null)
            return;

        await using var context = new ShopDbContext(
            new DbContextOptionsBuilder<ShopDbContext>().UseNpgsql(ConnectionString).Options);

        await context.Database.ExecuteSqlRawAsync("DROP SCHEMA IF EXISTS shop CASCADE");
        await context.Database.ExecuteSqlRawAsync("CREATE SCHEMA IF NOT EXISTS core");
        await context.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS core.\"user\" (id uuid PRIMARY KEY)");

        var applied = await MigrationRunner.RunAsync(context, new ShopInitializer(), NullLogger.Instance);
        var reapplied = await MigrationRunner.RunAsync(context, new ShopInitializer(), NullLogger.Instance);

        Assert.Equal(1, applied);
        Assert.Equal(0, reapplied);
        Assert.True(await context.DoesTableExistAsync("shop", "order"));
        Assert.True(await context.DoesTableExistAsync("shop", "order_line"));
        Assert.True(await context.DoesConstraintExistAsync("shop", "order", "order_owner_id_fkey"));
        Assert.True(await context.DoesIndexExistAsync("shop", "order_owner_reference_key"));
    }
}
