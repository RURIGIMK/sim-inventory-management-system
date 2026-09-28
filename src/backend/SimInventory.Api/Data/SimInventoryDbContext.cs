using Microsoft.EntityFrameworkCore;
using SimInventory.Api.Models;

namespace SimInventory.Api.Data;

public class SimInventoryDbContext(DbContextOptions<SimInventoryDbContext> options)
    : DbContext(options)
{
    public DbSet<Sim> Sims => Set<Sim>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Sim>(entity =>
        {
            entity.HasIndex(x => x.Iccid).IsUnique();
            entity.HasIndex(x => x.PhoneNumber).IsUnique();

            entity.Property(x => x.MonthlyCost).HasPrecision(12, 2);
            entity.Property(x => x.Status)
                .HasConversion<string>()
                .HasMaxLength(30);
        });
    }
}
