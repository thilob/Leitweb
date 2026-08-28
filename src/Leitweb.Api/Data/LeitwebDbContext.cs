using Leitweb.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Leitweb.Api.Data;

public sealed class LeitwebDbContext : DbContext
{
    public LeitwebDbContext(DbContextOptions<LeitwebDbContext> options) : base(options) { }
    public DbSet<Incident> Incidents => Set<Incident>();
    public DbSet<OperationalResource> Resources => Set<OperationalResource>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresEnum<IncidentStatus>();
        modelBuilder.HasPostgresEnum<ResourceStatus>();
        modelBuilder.Entity<Incident>(entity =>
        {
            entity.ToTable("incidents"); entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.OrganizationId, x.ReferenceNumber }).IsUnique();
            entity.Property(x => x.ReferenceNumber).HasMaxLength(50); entity.Property(x => x.Title).HasMaxLength(200);
            entity.Property(x => x.Location).HasMaxLength(500);
        });
        modelBuilder.Entity<OperationalResource>(entity =>
        {
            entity.ToTable("operational_resources"); entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.OrganizationId, x.CallSign }).IsUnique();
            entity.Property(x => x.CallSign).HasMaxLength(50); entity.Property(x => x.Name).HasMaxLength(200);
        });
        modelBuilder.Entity<IncidentStatusEntry>(entity =>
        {
            entity.ToTable("incident_status_history"); entity.HasKey(x => x.Id); entity.Property(x => x.ChangedBy).HasMaxLength(200);
        });
        modelBuilder.Entity<IncidentResource>(entity =>
        {
            entity.ToTable("incident_resources"); entity.HasKey(x => new { x.IncidentId, x.ResourceId });
        });
    }
}
