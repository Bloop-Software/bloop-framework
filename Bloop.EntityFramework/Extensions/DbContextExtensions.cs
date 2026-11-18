using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using System.Linq.Expressions;
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

            public async Task<bool> DoesConstraintExistAsync(string schema, string table, string constraintName)
            {
                var count = await dbContext.Database
                    .SqlQueryRaw<int>(
                        $"SELECT COUNT(*)::int AS \"Value\" FROM information_schema.table_constraints WHERE table_schema = {{0}} AND table_name = {{1}} AND constraint_name = {{2}}",
                        schema, table, constraintName)
                    .FirstAsync();
                return count > 0;
            }

            public async Task AddPrimaryKeyAsync(string schema, string table, string constraintName, string[] columns)
            {
                var columnList = string.Join(", ", columns.Select(c => Q(c)));
                await dbContext.Database.ExecuteSqlRawAsync(
                    $"ALTER TABLE {Q(schema)}.{Q(table)} ADD CONSTRAINT {Q(constraintName)} PRIMARY KEY ({columnList})");
            }

            public async Task SetColumnDefaultAsync(string schema, string table, string column, string defaultValue)
            {
                await dbContext.Database.ExecuteSqlRawAsync(
                    $"ALTER TABLE {Q(schema)}.{Q(table)} ALTER COLUMN {Q(column)} SET DEFAULT {defaultValue}");
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
                    .Where(et => et.GetTableName() != null && !et.IsTableExcludedFromMigrations())
                    .ToList();

                foreach (var entityType in entityTypes)
                    await CreateTableFromEntityTypeAsync(dbContext, entityType);

                foreach (var entityType in entityTypes)
                    await CreateForeignKeysForEntityTypeAsync(dbContext, entityType);
            }

            // Creates every listed table before any foreign key, so the entities may
            // reference each other in any order.
            public async Task CreateTablesFromModelAsync(Type[] entityClrTypes)
            {
                var model = GetDesignTimeModel(dbContext);
                var entityTypes = entityClrTypes.Select(t => FindEntityType(model, t)).ToList();

                foreach (var entityType in entityTypes)
                    await CreateTableFromEntityTypeAsync(dbContext, entityType);

                foreach (var entityType in entityTypes)
                    await CreateForeignKeysForEntityTypeAsync(dbContext, entityType);
            }

            public async Task AddColumnFromModelAsync<TEntity>(Expression<Func<TEntity, object?>> property)
                where TEntity : class
            {
                var entityType = FindEntityType(GetDesignTimeModel(dbContext), typeof(TEntity));
                await dbContext.Database.ExecuteSqlRawAsync(
                    BuildAddColumnSql(entityType, FindProperty(entityType, property)));
            }

            public async Task AlterColumnTypeFromModelAsync<TEntity>(Expression<Func<TEntity, object?>> property)
                where TEntity : class
            {
                var entityType = FindEntityType(GetDesignTimeModel(dbContext), typeof(TEntity));
                await dbContext.Database.ExecuteSqlRawAsync(
                    BuildAlterColumnTypeSql(entityType, FindProperty(entityType, property)));
            }

            public async Task CreateIndexFromModelAsync<TEntity>(string indexName) where TEntity : class
            {
                var entityType = FindEntityType(GetDesignTimeModel(dbContext), typeof(TEntity));
                var index = entityType.GetIndexes().FirstOrDefault(i => i.GetDatabaseName() == indexName)
                    ?? throw new InvalidOperationException(
                        $"Index '{indexName}' is not defined on entity type '{typeof(TEntity).Name}'");
                await dbContext.Database.ExecuteSqlRawAsync(BuildCreateIndexSql(entityType, index));
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

        private static IReadOnlyEntityType FindEntityType(IModel model, Type clrType)
        {
            var entityType = model.FindEntityType(clrType)
                ?? throw new InvalidOperationException(
                    $"Entity type '{clrType.Name}' is not part of the model");

            if (entityType.IsTableExcludedFromMigrations())
                throw new InvalidOperationException(
                    $"Entity type '{clrType.Name}' is excluded from migrations; its table is owned elsewhere");

            return entityType;
        }

        private static IReadOnlyProperty FindProperty<TEntity>(
            IReadOnlyEntityType entityType, Expression<Func<TEntity, object?>> property)
        {
            var body = property.Body is UnaryExpression { NodeType: ExpressionType.Convert } convert
                ? convert.Operand
                : property.Body;

            if (body is not MemberExpression member)
                throw new ArgumentException("Expression must select a single property", nameof(property));

            return entityType.FindProperty(member.Member.Name)
                ?? throw new InvalidOperationException(
                    $"Property '{member.Member.Name}' is not mapped on '{typeof(TEntity).Name}'");
        }

        private static string TableName(IReadOnlyEntityType entityType) =>
            $"{Q(entityType.GetSchema() ?? "public")}.{Q(entityType.GetTableName()!)}";

        private static string ColumnName(IReadOnlyProperty property) =>
            property.GetColumnName()
            ?? throw new InvalidOperationException(
                $"Cannot determine column name for property '{property.Name}'");

        internal static string BuildColumnDefinition(IReadOnlyProperty property)
        {
            var sb = new StringBuilder();
            sb.Append($"{Q(ColumnName(property))} {GetStoreType(property)}");

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
            else if (property.TryGetDefaultValue(out var defaultValue) && defaultValue != null
                && property.FindTypeMapping() is RelationalTypeMapping mapping)
                sb.Append($" DEFAULT {mapping.GenerateSqlLiteral(defaultValue)}");

            return sb.ToString();
        }

        internal static string BuildCreateTableSql(IReadOnlyEntityType entityType)
        {
            var columnDefs = entityType.GetProperties().Select(BuildColumnDefinition).ToList();

            var primaryKey = entityType.FindPrimaryKey();
            if (primaryKey != null)
            {
                var constraintName = primaryKey.GetName()!;
                var pkColumns = primaryKey.Properties.Select(p => Q(p.GetColumnName()!));
                columnDefs.Add(
                    $"CONSTRAINT {Q(constraintName)} PRIMARY KEY ({string.Join(", ", pkColumns)})");
            }

            return $"CREATE TABLE {TableName(entityType)} (\n  {string.Join(",\n  ", columnDefs)}\n)";
        }

        internal static string BuildCreateIndexSql(IReadOnlyEntityType entityType, IReadOnlyIndex index)
        {
            var indexName = index.GetDatabaseName()
                ?? $"ix_{entityType.GetTableName()}_{string.Join("_", index.Properties.Select(p => p.GetColumnName()))}";
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
            return $"CREATE {uniqueStr}INDEX {Q(indexName)} ON {TableName(entityType)} ({string.Join(", ", indexColumns)})";
        }

        internal static string BuildAddColumnSql(IReadOnlyEntityType entityType, IReadOnlyProperty property) =>
            $"ALTER TABLE {TableName(entityType)} ADD COLUMN {BuildColumnDefinition(property)}";

        internal static string BuildAlterColumnTypeSql(IReadOnlyEntityType entityType, IReadOnlyProperty property) =>
            $"ALTER TABLE {TableName(entityType)} ALTER COLUMN {Q(ColumnName(property))} TYPE {GetStoreType(property)}";

        internal static IEnumerable<string> BuildForeignKeySql(IReadOnlyEntityType entityType)
        {
            foreach (var fk in entityType.GetForeignKeys())
            {
                var constraintName = fk.GetConstraintName();
                if (constraintName == null) continue;

                var columns = fk.Properties.Select(p => Q(p.GetColumnName()!));
                var principalColumns = fk.PrincipalKey.Properties.Select(p => Q(p.GetColumnName()!));

                yield return
                    $"ALTER TABLE {TableName(entityType)} " +
                    $"ADD CONSTRAINT {Q(constraintName)} " +
                    $"FOREIGN KEY ({string.Join(", ", columns)}) " +
                    $"REFERENCES {TableName(fk.PrincipalEntityType)} ({string.Join(", ", principalColumns)})" +
                    MapDeleteBehavior(fk.DeleteBehavior);
            }
        }

        private static async Task CreateTableFromEntityTypeAsync(DbContext context, IReadOnlyEntityType entityType)
        {
            await context.Database.ExecuteSqlRawAsync(BuildCreateTableSql(entityType));

            foreach (var index in entityType.GetIndexes())
                await context.Database.ExecuteSqlRawAsync(BuildCreateIndexSql(entityType, index));
        }

        private static async Task CreateForeignKeysForEntityTypeAsync(DbContext context, IReadOnlyEntityType entityType)
        {
            foreach (var sql in BuildForeignKeySql(entityType))
                await context.Database.ExecuteSqlRawAsync(sql);
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
