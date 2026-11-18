using Bloop.EntityFramework.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace Bloop.EntityFramework.Tests;

// Runs only when BLOOP_TEST_PG holds a PostgreSQL connection string whose user may create
// databases. The test works in a temporary database of its own and deletes it afterwards.
public class PostgresDdlTests
{
    private static readonly string? ServerConnectionString = Environment.GetEnvironmentVariable("BLOOP_TEST_PG");

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
        if (ServerConnectionString == null)
            return;

        var connectionString = new NpgsqlConnectionStringBuilder(ServerConnectionString)
        {
            Database = $"bloop_test_{Guid.NewGuid():N}",
        }.ConnectionString;

        await using var context = new ShopDbContext(
            new DbContextOptionsBuilder<ShopDbContext>().UseNpgsql(connectionString).Options);

        await context.GetService<IRelationalDatabaseCreator>().CreateAsync();

        try
        {
            await context.Database.ExecuteSqlRawAsync("CREATE SCHEMA core");
            await context.Database.ExecuteSqlRawAsync("CREATE TABLE core.\"user\" (id uuid PRIMARY KEY)");

            var applied = await MigrationRunner.RunAsync(context, new ShopInitializer(), NullLogger.Instance);
            var reapplied = await MigrationRunner.RunAsync(context, new ShopInitializer(), NullLogger.Instance);

            Assert.Equal(1, applied);
            Assert.Equal(0, reapplied);
            Assert.True(await context.DoesTableExistAsync("shop", "order"));
            Assert.True(await context.DoesTableExistAsync("shop", "order_line"));
            Assert.True(await context.DoesConstraintExistAsync("shop", "order", "order_owner_id_fkey"));
            Assert.True(await context.DoesIndexExistAsync("shop", "order_owner_reference_key"));
        }
        finally
        {
            await context.Database.EnsureDeletedAsync();
        }
    }
}
