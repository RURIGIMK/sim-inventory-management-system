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
    public DbSet<SimCostRecord> CostRecords => Set<SimCostRecord>();
    public DbSet<ModelVersion> ModelVersions => Set<ModelVersion>();
    public DbSet<AnomalyScore> AnomalyScores => Set<AnomalyScore>();
    public DbSet<AnomalyReview> AnomalyReviews => Set<AnomalyReview>();
    public DbSet<EmployeeSyncRun> EmployeeSyncRuns => Set<EmployeeSyncRun>();

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
            entity.Property(x => x.Department).HasMaxLength(120);
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
            entity.Property(x => x.Source).HasMaxLength(40);
        });

        modelBuilder.Entity<SimAssignment>(entity =>
        {
            entity.HasOne(x => x.Sim).WithMany().HasForeignKey(x => x.SimId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => x.SimId).IsUnique().HasFilter("\"ReturnedAt\" IS NULL");
            entity.HasIndex(x => x.EmployeeId);
            entity.Property(x => x.Notes).HasMaxLength(1000);
        });

        // Tables below are created by Data/Migrations/0002_*.sql; this mapping must match it.
        modelBuilder.Entity<SimCostRecord>(entity =>
        {
            entity.ToTable("SimCostRecords");
            entity.HasIndex(x => new { x.SimId, x.Period }).IsUnique();
            entity.Property(x => x.Amount).HasPrecision(12, 2);
            entity.Property(x => x.Source).HasMaxLength(40);
            entity.Property(x => x.SyntheticScenario).HasMaxLength(60);
        });

        modelBuilder.Entity<ModelVersion>(entity =>
        {
            entity.ToTable("ModelVersions");
            entity.HasIndex(x => x.Version).IsUnique();
            entity.Property(x => x.Version).HasMaxLength(60);
            entity.Property(x => x.Algorithm).HasMaxLength(80);
            entity.Property(x => x.Status).HasMaxLength(20);
            entity.Property(x => x.LabelSource).HasMaxLength(60);
            entity.Property(x => x.DatasetSha256).HasMaxLength(64);
            entity.Property(x => x.ArtifactSha256).HasMaxLength(64);
            entity.Property(x => x.GitCommit).HasMaxLength(64);
        });

        modelBuilder.Entity<AnomalyScore>(entity =>
        {
            entity.ToTable("AnomalyScores");
            entity.HasIndex(x => new { x.ModelVersionId, x.SimId, x.Period }).IsUnique();
        });

        modelBuilder.Entity<AnomalyReview>(entity =>
        {
            entity.ToTable("AnomalyReviews");
            entity.HasIndex(x => new { x.SimId, x.Period }).IsUnique();
            entity.Property(x => x.Decision).HasMaxLength(20);
            entity.Property(x => x.FlaggedBy).HasMaxLength(20);
            entity.Property(x => x.Note).HasMaxLength(1000);
        });

        modelBuilder.Entity<EmployeeSyncRun>(entity =>
        {
            entity.ToTable("EmployeeSyncRuns");
            entity.Property(x => x.Source).HasMaxLength(40);
            entity.Property(x => x.Mode).HasMaxLength(20);
            entity.Property(x => x.Outcome).HasMaxLength(30);
            entity.Property(x => x.TriggeredBy).HasMaxLength(320);
        });
    }
}
