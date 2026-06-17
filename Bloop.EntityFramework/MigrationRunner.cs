using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bloop.EntityFramework;

public static class MigrationRunner
{
    public static async Task<int> RunAsync(DbContext context, IDatabaseInitializer initializer, ILogger logger)
    {
        await EnsureMigrationTableAsync(context, initializer.Schema);
        var applied = await GetAppliedMigrationsAsync(context, initializer.Schema);

        var count = 0;
        foreach (var migration in initializer.Migrations)
        {
            if (applied.Contains(migration.Id))
                continue;

            logger.LogInformation("Applying migration '{MigrationId}' to schema '{Schema}'",
                migration.Id, initializer.Schema);

            await using var transaction = await context.Database.BeginTransactionAsync();
            await migration.Apply(context);
            await RecordMigrationAsync(context, initializer.Schema, migration.Id);
            await transaction.CommitAsync();

            count++;
        }

        return count;
    }

    private static async Task EnsureMigrationTableAsync(DbContext context, string schema)
    {
        var s = Q(schema);
        await context.Database.ExecuteSqlRawAsync($"CREATE SCHEMA IF NOT EXISTS {s}");
        await context.Database.ExecuteSqlRawAsync(
            $"CREATE TABLE IF NOT EXISTS {s}.{Q("_schema_migrations")} (" +
            "migration_id text PRIMARY KEY, " +
            "applied_at timestamptz NOT NULL DEFAULT now())");
    }

    private static async Task<HashSet<string>> GetAppliedMigrationsAsync(DbContext context, string schema)
    {
        var ids = await context.Database
            .SqlQueryRaw<string>($"SELECT migration_id AS \"Value\" FROM {Q(schema)}.{Q("_schema_migrations")}")
            .ToListAsync();
        return ids.ToHashSet();
    }

    private static async Task RecordMigrationAsync(DbContext context, string schema, string migrationId)
    {
        await context.Database.ExecuteSqlRawAsync(
            $"INSERT INTO {Q(schema)}.{Q("_schema_migrations")} (migration_id) VALUES ({{0}})",
            migrationId);
    }

    private static string Q(string identifier) =>
        $"\"{identifier.Replace("\"", "\"\"")}\"";
}
