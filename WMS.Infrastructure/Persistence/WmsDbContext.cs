using MassTransit;
using Microsoft.EntityFrameworkCore;
using WMS.Domain.Aggregates;

namespace WMS.Infrastructure.Persistence;

public class WmsDbContext : DbContext
{
    public DbSet<Inventory> Inventories => Set<Inventory>();

    public WmsDbContext(DbContextOptions<WmsDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Inventory>(b =>
        {
            b.ToTable("inventories");
            b.HasKey(i => i.Id);
            b.HasIndex(i => i.ProductId).IsUnique();
            b.Property(i => i.ProductId).IsRequired();
            b.Property(i => i.AvailableQuantity).IsRequired();
            b.Property(i => i.ReservedQuantity).IsRequired();
        });

        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}