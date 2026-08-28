namespace Leitweb.Api.Domain;

public sealed class OperationalResource
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public string CallSign { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public ResourceStatus Status { get; set; } = ResourceStatus.Available;
    public List<IncidentResource> Incidents { get; set; } = new();
}

public enum ResourceStatus { Unavailable, Available, Dispatched, OnScene }

public sealed class IncidentResource
{
    public Guid IncidentId { get; set; }
    public Incident Incident { get; set; } = null!;
    public Guid ResourceId { get; set; }
    public OperationalResource Resource { get; set; } = null!;
    public DateTimeOffset AssignedAt { get; set; } = DateTimeOffset.UtcNow;
}
