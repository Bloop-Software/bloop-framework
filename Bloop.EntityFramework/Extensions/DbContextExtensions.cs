using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using System.Text;

namespace Bloop.EntityFramework.Extensions
{
    public static class DbContextExtensions
    {
        extension(DbContext dbContext)
        {
            public async Task ExecuteInTransactionAsync(Func<Task> action, ILogger logger)
            {
                var strategy = dbContext.Database.CreateExecutionStrategy();
                await strategy.ExecuteAsync(async () =>
                {
                    await using var transaction = await dbContext.Database.BeginTransactionAsync();

                    try
                    {
                        await action();

                        await transaction.CommitAsync();
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Error executing transaction");
                        throw;
                    }
                });
            }

            public async Task CreateSchemaIfNotExistsAsync(string schema)
            {
                await dbContext.Database.ExecuteSqlRawAsync(
                    $"CREATE SCHEMA IF NOT EXISTS {Q(schema)}");
            }

            public async Task<bool> DoesTableExistAsync(string schema, string table)
            {
                var count = await dbContext.Database
                    .SqlQueryRaw<int>(
                        $"SELECT COUNT(*)::int AS \"Value\" FROM information_schema.tables WHERE table_schema = {{0}} AND table_name = {{1}}",
                        schema, table)
                    .FirstAsync();
                return count > 0;
            }

            public async Task<bool> DoesColumnExistAsync(string schema, string table, string column)
            {
                var count = await dbContext.Database
                    .SqlQueryRaw<int>(
                        $"SELECT COUNT(*)::int AS \"Value\" FROM information_schema.columns WHERE table_schema = {{0}} AND table_name = {{1}} AND column_name = {{2}}",
                        schema, table, column)
                    .FirstAsync();
                return count > 0;
            }

            public async Task<bool> DoesIndexExistAsync(string schema, string indexName)
            {
                var count = await dbContext.Database
                    .SqlQueryRaw<int>(
                        $"SELECT COUNT(*)::int AS \"Value\" FROM pg_indexes WHERE schemaname = {{0}} AND indexname = {{1}}",
                        schema, indexName)
                    .FirstAsync();
                return count > 0;
            }

            public async Task AddColumnAsync(string schema, string table, string column, string type,
                bool nullable = true, string? defaultValue = null)
            {
                var sb = new StringBuilder();
                sb.Append($"ALTER TABLE {Q(schema)}.{Q(table)} ADD COLUMN {Q(column)} {type}");

                if (!nullable)
                    sb.Append(" NOT NULL");

                if (defaultValue != null)
                    sb.Append($" DEFAULT {defaultValue}");

                await dbContext.Database.ExecuteSqlRawAsync(sb.ToString());
            }

            public async Task DropColumnAsync(string schema, string table, string column)
            {
                await dbContext.Database.ExecuteSqlRawAsync(
                    $"ALTER TABLE {Q(schema)}.{Q(table)} DROP COLUMN {Q(column)}");
            }

            public async Task AddIndexAsync(string schema, string table, string[] columns,
                bool unique = false, string? indexName = null)
            {
                indexName ??= $"ix_{table}_{string.Join("_", columns)}";
                var uniqueStr = unique ? "UNIQUE " : "";
                var columnList = string.Join(", ", columns.Select(c => Q(c)));

                await dbContext.Database.ExecuteSqlRawAsync(
                    $"CREATE {uniqueStr}INDEX {Q(indexName)} ON {Q(schema)}.{Q(table)} ({columnList})");
            }

            public async Task DropIndexAsync(string schema, string indexName)
            {
                await dbContext.Database.ExecuteSqlRawAsync(
                    $"DROP INDEX {Q(schema)}.{Q(indexName)}");
            }

            public async Task CreateTableFromModelAsync<TEntity>() where TEntity : class
            {
                var model = GetDesignTimeModel(dbContext);
                var entityType = model.FindEntityType(typeof(TEntity))
                    ?? throw new InvalidOperationException(
                        $"Entity type '{typeof(TEntity).Name}' is not part of the model");
                await CreateTableFromEntityTypeAsync(dbContext, entityType);
            }

            public async Task CreateTableFromModelAsync(string entityName)
            {
                var model = GetDesignTimeModel(dbContext);
                var entityType = model.FindEntityType(entityName)
                    ?? throw new InvalidOperationException(
                        $"Entity type '{entityName}' is not part of the model");
                await CreateTableFromEntityTypeAsync(dbContext, entityType);
                await CreateForeignKeysForEntityTypeAsync(dbContext, entityType);
            }

            public async Task CreateForeignKeysFromModelAsync<TEntity>() where TEntity : class
            {
                var model = GetDesignTimeModel(dbContext);
                var entityType = model.FindEntityType(typeof(TEntity))
                    ?? throw new InvalidOperationException(
                        $"Entity type '{typeof(TEntity).Name}' is not part of the model");
                await CreateForeignKeysForEntityTypeAsync(dbContext, entityType);
            }

            public async Task CreateAllTablesFromModelAsync()
            {
                var model = GetDesignTimeModel(dbContext);
                var entityTypes = model.GetEntityTypes()
                    .Where(et => et.GetTableName() != null)
                    .ToList();

                foreach (var entityType in entityTypes)
                    await CreateTableFromEntityTypeAsync(dbContext, entityType);

                foreach (var entityType in entityTypes)
                    await CreateForeignKeysForEntityTypeAsync(dbContext, entityType);
            }
        }

        private static IModel GetDesignTimeModel(DbContext context) =>
            context.GetService<IDesignTimeModel>().Model;

        private static string Q(string identifier) =>
            $"\"{identifier.Replace("\"", "\"\"")}\"";

        private static string GetStoreType(IReadOnlyProperty property) =>
            property.GetColumnType()
            ?? (property.FindTypeMapping() as RelationalTypeMapping)?.StoreType
            ?? throw new InvalidOperationException(
                $"Cannot determine store type for property '{property.DeclaringType.ClrType.Name}.{property.Name}'");

        private static async Task CreateTableFromEntityTypeAsync(DbContext context, IReadOnlyEntityType entityType)
        {
            var tableName = entityType.GetTableName()!;
            var schema = entityType.GetSchema() ?? "public";
            var columnDefs = new List<string>();

            foreach (var property in entityType.GetProperties())
            {
                var columnName = property.GetColumnName()
                    ?? throw new InvalidOperationException(
                        $"Cannot determine column name for property '{property.Name}'");
                var storeType = GetStoreType(property);

                var sb = new StringBuilder();
                sb.Append($"{Q(columnName)} {storeType}");

                var strategy = property.FindAnnotation("Npgsql:ValueGenerationStrategy")?.Value?.ToString();
                if (strategy == "IdentityAlwaysColumn")
                    sb.Append(" GENERATED ALWAYS AS IDENTITY");
                else if (strategy == "IdentityByDefaultColumn")
                    sb.Append(" GENERATED BY DEFAULT AS IDENTITY");

                if (!property.IsNullable)
                    sb.Append(" NOT NULL");

                var collation = property.GetCollation();
                if (collation != null)
                    sb.Append($" COLLATE {Q(collation)}");

                var defaultValueSql = property.GetDefaultValueSql();
                if (defaultValueSql != null)
                    sb.Append($" DEFAULT {defaultValueSql}");

                columnDefs.Add(sb.ToString());
            }

            var primaryKey = entityType.FindPrimaryKey();
            if (primaryKey != null)
            {
                var constraintName = primaryKey.GetName()!;
                var pkColumns = primaryKey.Properties.Select(p => Q(p.GetColumnName()!));
                columnDefs.Add(
                    $"CONSTRAINT {Q(constraintName)} PRIMARY KEY ({string.Join(", ", pkColumns)})");
            }

            var createSql =
                $"CREATE TABLE {Q(schema)}.{Q(tableName)} (\n  {string.Join(",\n  ", columnDefs)}\n)";
            await context.Database.ExecuteSqlRawAsync(createSql);

            foreach (var index in entityType.GetIndexes())
            {
                var indexName = index.GetDatabaseName()
                    ?? $"ix_{tableName}_{string.Join("_", index.Properties.Select(p => p.GetColumnName()))}";
                var collations = index.FindAnnotation("Relational:Collation")?.Value
                    as IReadOnlyList<string?>;

                var indexColumns = index.Properties.Select((p, i) =>
                {
                    var colRef = Q(p.GetColumnName()!);
                    if (collations != null && i < collations.Count && collations[i] != null)
                        colRef += $" COLLATE {Q(collations[i]!)}";
                    return colRef;
                });

                var uniqueStr = index.IsUnique ? "UNIQUE " : "";
                var indexSql =
                    $"CREATE {uniqueStr}INDEX {Q(indexName)} ON {Q(schema)}.{Q(tableName)} ({string.Join(", ", indexColumns)})";
                await context.Database.ExecuteSqlRawAsync(indexSql);
            }
        }

        private static async Task CreateForeignKeysForEntityTypeAsync(DbContext context, IReadOnlyEntityType entityType)
        {
            var tableName = entityType.GetTableName()!;
            var schema = entityType.GetSchema() ?? "public";

            foreach (var fk in entityType.GetForeignKeys())
            {
                var constraintName = fk.GetConstraintName();
                if (constraintName == null) continue;

                var principalTable = fk.PrincipalEntityType.GetTableName()!;
                var principalSchema = fk.PrincipalEntityType.GetSchema() ?? "public";

                var columns = fk.Properties.Select(p => Q(p.GetColumnName()!));
                var principalColumns = fk.PrincipalKey.Properties.Select(p => Q(p.GetColumnName()!));
                var onDelete = MapDeleteBehavior(fk.DeleteBehavior);

                var sql =
                    $"ALTER TABLE {Q(schema)}.{Q(tableName)} " +
                    $"ADD CONSTRAINT {Q(constraintName)} " +
                    $"FOREIGN KEY ({string.Join(", ", columns)}) " +
                    $"REFERENCES {Q(principalSchema)}.{Q(principalTable)} ({string.Join(", ", principalColumns)})" +
                    onDelete;

                await context.Database.ExecuteSqlRawAsync(sql);
            }
        }

        private static string MapDeleteBehavior(DeleteBehavior behavior) => behavior switch
        {
            DeleteBehavior.Cascade => " ON DELETE CASCADE",
            DeleteBehavior.SetNull => " ON DELETE SET NULL",
            DeleteBehavior.Restrict => " ON DELETE RESTRICT",
            DeleteBehavior.NoAction => " ON DELETE NO ACTION",
            _ => "",
        };
    }
}
