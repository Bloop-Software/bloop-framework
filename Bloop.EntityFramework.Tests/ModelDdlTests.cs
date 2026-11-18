using Bloop.EntityFramework.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace Bloop.EntityFramework.Tests;

// Asserts the generated DDL text. Execution against PostgreSQL is covered by PostgresDdlTests.
public class ModelDdlTests
{
    private static readonly IModel Model = ShopDbContext.Create().GetService<IDesignTimeModel>().Model;

    private static IEntityType Entity<T>() => Model.FindEntityType(typeof(T))!;

    [Fact]
    public void BuildCreateTableSql_EmitsColumnsDefaultsAndPrimaryKey()
    {
        var sql = DbContextExtensions.BuildCreateTableSql(Entity<Order>());

        Assert.StartsWith("CREATE TABLE \"shop\".\"order\" (", sql);
        Assert.Contains("\"id\" uuid NOT NULL", sql);
        Assert.Contains("\"reference\" character varying(40) NOT NULL", sql);
        Assert.Contains("\"notes\" text,", sql);
        Assert.Contains("\"quantity\" integer NOT NULL DEFAULT 1", sql);
        Assert.Contains("\"created_at\" timestamp with time zone NOT NULL DEFAULT now()", sql);
        Assert.Contains("CONSTRAINT \"order_pkey\" PRIMARY KEY (\"id\")", sql);
    }

    [Fact]
    public void BuildCreateTableSql_IdentityColumn()
    {
        var sql = DbContextExtensions.BuildCreateTableSql(Entity<OrderLine>());

        Assert.Contains("\"id\" bigint GENERATED ALWAYS AS IDENTITY NOT NULL", sql);
    }

    [Fact]
    public void BuildForeignKeySql_ReferencesTableInAnotherSchema()
    {
        var sql = Assert.Single(DbContextExtensions.BuildForeignKeySql(Entity<Order>()));

        Assert.Equal(
            "ALTER TABLE \"shop\".\"order\" ADD CONSTRAINT \"order_owner_id_fkey\" " +
            "FOREIGN KEY (\"owner_id\") REFERENCES \"core\".\"user\" (\"id\") ON DELETE RESTRICT",
            sql);
    }

    [Fact]
    public void BuildForeignKeySql_Cascade()
    {
        var sql = Assert.Single(DbContextExtensions.BuildForeignKeySql(Entity<OrderLine>()));

        Assert.EndsWith("REFERENCES \"shop\".\"order\" (\"id\") ON DELETE CASCADE", sql);
    }

    [Fact]
    public void BuildCreateIndexSql_UniqueCompositeIndex()
    {
        var entity = Entity<Order>();
        var index = entity.GetIndexes().Single(i => i.GetDatabaseName() == "order_owner_reference_key");

        Assert.Equal(
            "CREATE UNIQUE INDEX \"order_owner_reference_key\" ON \"shop\".\"order\" (\"owner_id\", \"reference\")",
            DbContextExtensions.BuildCreateIndexSql(entity, index));
    }

    [Fact]
    public void BuildAddColumnSql_UsesModelDefinition()
    {
        var entity = Entity<Order>();

        Assert.Equal(
            "ALTER TABLE \"shop\".\"order\" ADD COLUMN \"quantity\" integer NOT NULL DEFAULT 1",
            DbContextExtensions.BuildAddColumnSql(entity, entity.FindProperty(nameof(Order.Quantity))!));
    }

    [Fact]
    public void BuildAlterColumnTypeSql_UsesModelStoreType()
    {
        var entity = Entity<Order>();

        Assert.Equal(
            "ALTER TABLE \"shop\".\"order\" ALTER COLUMN \"reference\" TYPE character varying(40)",
            DbContextExtensions.BuildAlterColumnTypeSql(entity, entity.FindProperty(nameof(Order.Reference))!));
    }

    [Fact]
    public async Task CreateTablesFromModelAsync_RefusesTablesOwnedElsewhere()
    {
        await using var context = ShopDbContext.Create();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => context.CreateTablesFromModelAsync([typeof(OwnerReference)]));

        Assert.Contains("excluded from migrations", ex.Message);
    }

    [Fact]
    public async Task AddColumnFromModelAsync_UnmappedProperty_Throws()
    {
        await using var context = ShopDbContext.Create();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => context.AddColumnFromModelAsync<Order>(o => o.Lines));
    }
}
