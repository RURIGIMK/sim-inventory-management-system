using Microsoft.EntityFrameworkCore;
using SimInventory.Api.Models;

namespace SimInventory.Api.Data;

public class SimInventoryDbContext(DbContextOptions<SimInventoryDbContext> options)
    : DbContext(options)
{
    public DbSet<Sim> Sims => Set<Sim>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<SimAssignment> Assignments => Set<SimAssignment>();

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

        modelBuilder.Entity<Employee>(entity =>
        {
            entity.HasIndex(x => x.EmployeeNumber).IsUnique();
            entity.Property(x => x.EmployeeNumber).HasMaxLength(80).IsRequired();
            entity.Property(x => x.FullName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Email).HasMaxLength(320);
            entity.Property(x => x.Department).HasMaxLength(120);
            entity.Property(x => x.CostCentre).HasMaxLength(80);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
        });

        modelBuilder.Entity<SimAssignment>(entity =>
        {
            entity.HasOne(x => x.Sim).WithMany().HasForeignKey(x => x.SimId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => x.SimId).IsUnique().HasFilter("\"ReturnedAt\" IS NULL");
            entity.HasIndex(x => x.EmployeeId);
            entity.Property(x => x.Notes).HasMaxLength(1000);
        });
    }
}
