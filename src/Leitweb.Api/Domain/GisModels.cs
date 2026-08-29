using NetTopologySuite.Geometries;

namespace Leitweb.Api.Domain;

public sealed class GisLayer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Color { get; set; } = "#175e96";
    public bool IsEditable { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<GisFeature> Features { get; set; } = new List<GisFeature>();
}

public sealed class GisFeature
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid GisLayerId { get; set; }
    public GisLayer Layer { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Geometry Geometry { get; set; } = null!;
    public string PropertiesJson { get; set; } = "{}";
    public string UpdatedBy { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class GisSource
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ServiceType { get; set; } = "WMS";
    public string ServiceUrl { get; set; } = string.Empty;
    public string LayerName { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class GisMapProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public string UserSubject { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ConfigurationJson { get; set; } = "{}";
    public bool IsDefault { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
