using Leitweb.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Leitweb.Api.Data;

public sealed class LeitwebDbContext : DbContext
{
    public LeitwebDbContext(DbContextOptions<LeitwebDbContext> options) : base(options) { }
    public DbSet<Incident> Incidents => Set<Incident>();
    public DbSet<OperationalResource> Resources => Set<OperationalResource>();
    public DbSet<PoliceCase> Cases => Set<PoliceCase>();
    public DbSet<CasePerson> CasePersons => Set<CasePerson>();
    public DbSet<EvidenceItem> EvidenceItems => Set<EvidenceItem>();
    public DbSet<CaseDocument> CaseDocuments => Set<CaseDocument>();
    public DbSet<DocumentDispatch> DocumentDispatches => Set<DocumentDispatch>();
    public DbSet<AddressEntry> Addresses => Set<AddressEntry>();
    public DbSet<GisLayer> GisLayers => Set<GisLayer>();
    public DbSet<GisFeature> GisFeatures => Set<GisFeature>();
    public DbSet<GisSource> GisSources => Set<GisSource>();
    public DbSet<GisMapProfile> GisMapProfiles => Set<GisMapProfile>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresEnum<IncidentStatus>();
        modelBuilder.HasPostgresEnum<ResourceStatus>();
        modelBuilder.HasPostgresEnum<PoliceOccasion>();
        modelBuilder.HasPostgresEnum<CaseStatus>();
        modelBuilder.HasPostgresEnum<PersonRole>();
        modelBuilder.HasPostgresEnum<EvidenceStatus>();
        modelBuilder.HasPostgresEnum<DocumentType>();
        modelBuilder.HasPostgresEnum<DispatchStatus>();
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
        modelBuilder.Entity<PoliceCase>(entity =>
        {
            entity.ToTable("police_cases"); entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.OrganizationId, x.FileNumber }).IsUnique();
            entity.HasIndex(x => x.IncidentId).IsUnique();
            entity.Property(x => x.FileNumber).HasMaxLength(80); entity.Property(x => x.Subject).HasMaxLength(300);
        });
        modelBuilder.Entity<CasePerson>(entity =>
        {
            entity.ToTable("case_persons"); entity.HasKey(x => x.Id);
            entity.Property(x => x.FirstName).HasMaxLength(100); entity.Property(x => x.LastName).HasMaxLength(100);
            entity.Property(x => x.Address).HasMaxLength(400); entity.Property(x => x.Contact).HasMaxLength(200);
        });
        modelBuilder.Entity<EvidenceItem>(entity =>
        {
            entity.ToTable("evidence_items"); entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.PoliceCaseId, x.EvidenceNumber }).IsUnique();
            entity.Property(x => x.EvidenceNumber).HasMaxLength(80); entity.Property(x => x.StorageLocation).HasMaxLength(200);
        });
        modelBuilder.Entity<CaseDocument>(entity =>
        {
            entity.ToTable("case_documents"); entity.HasKey(x => x.Id); entity.Property(x => x.Title).HasMaxLength(300);
        });
        modelBuilder.Entity<DocumentDispatch>(entity =>
        {
            entity.ToTable("document_dispatches"); entity.HasKey(x => x.Id);
            entity.Property(x => x.Recipient).HasMaxLength(300); entity.Property(x => x.Reference).HasMaxLength(150);
        });
        modelBuilder.Entity<AddressEntry>(entity =>
        {
            entity.ToTable("address_register"); entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.Municipality, x.Street, x.HouseNumber }).IsUnique();
            entity.HasIndex(x => x.Street);
            entity.Property(x => x.Municipality).HasMaxLength(120); entity.Property(x => x.PostalCode).HasMaxLength(10);
            entity.Property(x => x.Street).HasMaxLength(200); entity.Property(x => x.HouseNumber).HasMaxLength(30);
            entity.Ignore(x => x.DisplayName);
        });
        modelBuilder.Entity<GisLayer>(entity =>
        {
            entity.ToTable("gis_layers"); entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.OrganizationId, x.Name }).IsUnique();
            entity.Property(x => x.Name).HasMaxLength(150); entity.Property(x => x.Description).HasMaxLength(500);
            entity.Property(x => x.Color).HasMaxLength(20);
        });
        modelBuilder.Entity<GisFeature>(entity =>
        {
            entity.ToTable("gis_features"); entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.OrganizationId); entity.HasIndex(x => x.GisLayerId);
            entity.HasIndex(x => x.Geometry).HasMethod("gist");
            entity.Property(x => x.Name).HasMaxLength(200); entity.Property(x => x.Description).HasMaxLength(1000);
            entity.Property(x => x.Geometry).HasColumnType("geometry(Geometry,4326)");
            entity.Property(x => x.PropertiesJson).HasColumnType("jsonb"); entity.Property(x => x.UpdatedBy).HasMaxLength(200);
            entity.HasOne(x => x.Layer).WithMany(x => x.Features).HasForeignKey(x => x.GisLayerId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<GisSource>(entity =>
        {
            entity.ToTable("gis_sources"); entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.OrganizationId, x.Name }).IsUnique();
            entity.Property(x => x.Name).HasMaxLength(150); entity.Property(x => x.ServiceType).HasMaxLength(10);
            entity.Property(x => x.ServiceUrl).HasMaxLength(2000); entity.Property(x => x.LayerName).HasMaxLength(500);
        });
        modelBuilder.Entity<GisMapProfile>(entity =>
        {
            entity.ToTable("gis_map_profiles"); entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.OrganizationId, x.UserSubject, x.Name }).IsUnique();
            entity.Property(x => x.UserSubject).HasMaxLength(200); entity.Property(x => x.Name).HasMaxLength(150);
            entity.Property(x => x.ConfigurationJson).HasColumnType("jsonb");
        });
    }
}
