using Microsoft.EntityFrameworkCore;

namespace Bloop.EntityFramework.Tests;

public class OwnerReference
{
    public Guid Id { get; set; }
}

public class Order
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string Reference { get; set; } = "";
    public string? Notes { get; set; }
    public int Quantity { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<OrderLine> Lines { get; set; } = [];
}

public class OrderLine
{
    public long Id { get; set; }
    public Guid OrderId { get; set; }
    public string Sku { get; set; } = "";
}

public class ShopDbContext(DbContextOptions<ShopDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    public static ShopDbContext Create() =>
        new(new DbContextOptionsBuilder<ShopDbContext>()
            .UseNpgsql("Host=unused;Database=unused")
            .Options);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("shop");

        modelBuilder.Entity<OwnerReference>(entity =>
        {
            entity.ToTable("user", "core", table => table.ExcludeFromMigrations());
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
        });

        modelBuilder.Entity<Order>(entity =>
        {
            entity.ToTable("order");
            entity.HasKey(e => e.Id).HasName("order_pkey");
            entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(e => e.OwnerId).HasColumnName("owner_id");
            entity.Property(e => e.Reference).HasColumnName("reference").HasMaxLength(40);
            entity.Property(e => e.Notes).HasColumnName("notes");
            entity.Property(e => e.Quantity).HasColumnName("quantity").HasDefaultValue(1);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            entity.HasIndex(e => new { e.OwnerId, e.Reference }).IsUnique().HasDatabaseName("order_owner_reference_key");
            entity.HasIndex(e => e.CreatedAt).HasDatabaseName("order_created_at_idx");
            entity.HasOne<OwnerReference>().WithMany()
                .HasForeignKey(e => e.OwnerId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("order_owner_id_fkey");
        });

        modelBuilder.Entity<OrderLine>(entity =>
        {
            entity.ToTable("order_line");
            entity.HasKey(e => e.Id).HasName("order_line_pkey");
            entity.Property(e => e.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            entity.Property(e => e.OrderId).HasColumnName("order_id");
            entity.Property(e => e.Sku).HasColumnName("sku").HasMaxLength(20);
            entity.HasOne<Order>().WithMany(o => o.Lines)
                .HasForeignKey(e => e.OrderId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("order_line_order_id_fkey");
        });
    }
}
