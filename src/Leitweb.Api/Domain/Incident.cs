namespace Leitweb.Api.Domain;

public sealed class Incident
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public string ReferenceNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public PoliceOccasion Occasion { get; set; }
    public IncidentStatus Status { get; set; } = IncidentStatus.Open;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<IncidentStatusEntry> StatusHistory { get; set; } = new();
    public List<IncidentResource> AssignedResources { get; set; } = new();
}

public enum IncidentStatus { Open, Dispatched, InProgress, Closed, Cancelled }

public enum PoliceOccasion
{
    Other, TrafficAccident, Disturbance, Theft, Burglary, Assault, DomesticViolence,
    MissingPerson, SuspiciousPerson, PropertyDamage, TrafficControl, AdministrativeAssistance
}

public sealed class IncidentStatusEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid IncidentId { get; set; }
    public Incident Incident { get; set; } = null!;
    public IncidentStatus Status { get; set; }
    public DateTimeOffset ChangedAt { get; set; } = DateTimeOffset.UtcNow;
    public string ChangedBy { get; set; } = string.Empty;
}
