using Microsoft.EntityFrameworkCore;
using SimInventory.Api.Models;

namespace SimInventory.Api.Data;

public class SimInventoryDbContext(DbContextOptions<SimInventoryDbContext> options)
    : DbContext(options)
{
    public DbSet<Sim> Sims => Set<Sim>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Sim>(entity =>
        {
            entity.HasIndex(x => x.Iccid).IsUnique();
            entity.HasIndex(x => x.PhoneNumber).IsUnique();
            entity.Property(x => x.MonthlyCost).HasPrecision(12, 2);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
        });

        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.HasIndex(x => x.Email).IsUnique();
            entity.Property(x => x.Email).HasMaxLength(320).IsRequired();
            entity.Property(x => x.DisplayName).HasMaxLength(160).IsRequired();
            entity.Property(x => x.Role).HasConversion<string>().HasMaxLength(40);
        });

        modelBuilder.Entity<AuditEvent>(entity =>
        {
            entity.HasIndex(x => x.CreatedAt);
            entity.HasIndex(x => x.Hash).IsUnique();
            entity.Property(x => x.ActorEmail).HasMaxLength(320);
            entity.Property(x => x.Action).HasMaxLength(100);
            entity.Property(x => x.EntityType).HasMaxLength(100);
            entity.Property(x => x.EntityId).HasMaxLength(200);
            entity.Property(x => x.Hash).HasMaxLength(64);
            entity.Property(x => x.PreviousHash).HasMaxLength(64);
        });
    }
}
