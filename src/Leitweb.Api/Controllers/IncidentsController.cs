using Leitweb.Api.Data;
using Leitweb.Api.Domain;
using Leitweb.Api.Realtime;
using Leitweb.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Leitweb.Api.Controllers;

[ApiController, Route("api/v1/incidents")]
public sealed class IncidentsController : ControllerBase
{
    private readonly LeitwebDbContext _db;
    private readonly LiveUpdateHub _updates;
    public IncidentsController(LeitwebDbContext db, LiveUpdateHub updates) { _db = db; _updates = updates; }

    [HttpGet, Authorize(Policy = Permissions.IncidentRead)]
    public async Task<ActionResult<IReadOnlyList<IncidentSummary>>> GetAll([FromQuery] Guid organizationId, CancellationToken ct) =>
        await _db.Incidents.AsNoTracking().Where(x => x.OrganizationId == organizationId)
            .OrderByDescending(x => x.CreatedAt).Select(x => new IncidentSummary(
                x.Id, x.OrganizationId, x.ReferenceNumber, x.Title, x.Location, x.Occasion, x.Status, x.CreatedAt,
                x.AssignedResources.Count)).ToListAsync(ct);

    [HttpGet("{id:guid}"), Authorize(Policy = Permissions.IncidentRead)]
    public async Task<ActionResult<IncidentDetails>> Get(Guid id, CancellationToken ct)
    {
        var incident = await _db.Incidents.AsNoTracking().Where(x => x.Id == id).Select(x => new IncidentDetails(
            x.Id, x.OrganizationId, x.ReferenceNumber, x.Title, x.Description, x.Location, x.Occasion, x.Status,
            x.CreatedAt, x.UpdatedAt,
            x.StatusHistory.OrderByDescending(h => h.ChangedAt).Select(h => new StatusHistoryItem(h.Status, h.ChangedAt, h.ChangedBy)).ToList(),
            x.AssignedResources.OrderBy(a => a.Resource.CallSign).Select(a => new AssignedResource(
                a.ResourceId, a.Resource.CallSign, a.Resource.Name, a.Resource.Status, a.AssignedAt)).ToList()
        )).SingleOrDefaultAsync(ct);
        return incident is null ? NotFound() : incident;
    }

    [HttpPost, Authorize(Policy = Permissions.IncidentCreate)]
    public async Task<ActionResult<IncidentDetails>> Create(CreateIncident request, CancellationToken ct)
    {
        if (request.OrganizationId == Guid.Empty || string.IsNullOrWhiteSpace(request.ReferenceNumber)
            || string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Location))
            return ValidationProblem("Organisation, Einsatznummer, Titel und Einsatzort sind erforderlich.");

        var incident = new Incident
        {
            OrganizationId = request.OrganizationId, ReferenceNumber = request.ReferenceNumber.Trim(),
            Title = request.Title.Trim(), Description = request.Description.Trim(), Location = request.Location.Trim(), Occasion = request.Occasion
        };
        incident.StatusHistory.Add(new IncidentStatusEntry { Status = incident.Status, ChangedBy = User.Identity?.Name ?? "unknown" });
        _db.Incidents.Add(incident); await _db.SaveChangesAsync(ct);
        await _updates.BroadcastAsync("incidents.changed");
        return CreatedAtAction(nameof(Get), new { id = incident.Id }, null);
    }

    [HttpPut("{id:guid}"), Authorize(Policy = Permissions.IncidentUpdate)]
    public async Task<IActionResult> Update(Guid id, UpdateIncident request, CancellationToken ct)
    {
        var incident = await _db.Incidents.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (incident is null) return NotFound();
        incident.Title = request.Title.Trim(); incident.Description = request.Description.Trim();
        incident.Location = request.Location.Trim(); incident.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct); await _updates.BroadcastAsync("incidents.changed"); return NoContent();
    }

    [HttpPut("{id:guid}/status"), Authorize(Policy = Permissions.IncidentUpdate)]
    public async Task<IActionResult> ChangeStatus(Guid id, ChangeIncidentStatus request, CancellationToken ct)
    {
        var incident = await _db.Incidents.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (incident is null) return NotFound();
        if (incident.Status == request.Status) return NoContent();
        incident.Status = request.Status; incident.UpdatedAt = DateTimeOffset.UtcNow;
        _db.Add(new IncidentStatusEntry { IncidentId = id, Status = request.Status, ChangedBy = User.Identity?.Name ?? "unknown" });
        await _db.SaveChangesAsync(ct); await _updates.BroadcastAsync("incidents.changed"); return NoContent();
    }

    [HttpPost("{id:guid}/resources/{resourceId:guid}"), Authorize(Policy = Permissions.IncidentUpdate)]
    public async Task<IActionResult> AssignResource(Guid id, Guid resourceId, CancellationToken ct)
    {
        var incident = await _db.Incidents.SingleOrDefaultAsync(x => x.Id == id, ct);
        var resource = await _db.Resources.SingleOrDefaultAsync(x => x.Id == resourceId, ct);
        if (incident is null || resource is null) return NotFound();
        if (incident.OrganizationId != resource.OrganizationId) return BadRequest("Einsatz und Einsatzmittel gehören nicht derselben Organisation an.");
        if (!await _db.Set<IncidentResource>().AnyAsync(x => x.IncidentId == id && x.ResourceId == resourceId, ct))
            _db.Add(new IncidentResource { IncidentId = id, ResourceId = resourceId });
        resource.Status = ResourceStatus.Dispatched;
        if (incident.Status == IncidentStatus.Open)
        {
            incident.Status = IncidentStatus.Dispatched;
            _db.Add(new IncidentStatusEntry { IncidentId = id, Status = incident.Status, ChangedBy = User.Identity?.Name ?? "unknown" });
        }
        await _db.SaveChangesAsync(ct); await _updates.BroadcastAsync("incidents-and-resources.changed"); return NoContent();
    }

    [HttpDelete("{id:guid}/resources/{resourceId:guid}"), Authorize(Policy = Permissions.IncidentUpdate)]
    public async Task<IActionResult> UnassignResource(Guid id, Guid resourceId, CancellationToken ct)
    {
        var assignment = await _db.Set<IncidentResource>().SingleOrDefaultAsync(x => x.IncidentId == id && x.ResourceId == resourceId, ct);
        if (assignment is null) return NotFound();
        _db.Remove(assignment);
        var resource = await _db.Resources.FindAsync(new object[] { resourceId }, ct);
        if (resource is not null) resource.Status = ResourceStatus.Available;
        await _db.SaveChangesAsync(ct); await _updates.BroadcastAsync("incidents-and-resources.changed"); return NoContent();
    }
}

public sealed record CreateIncident(Guid OrganizationId, string ReferenceNumber, string Title, string Description, string Location, PoliceOccasion Occasion);
public sealed record UpdateIncident(string Title, string Description, string Location);
public sealed record ChangeIncidentStatus(IncidentStatus Status);
public sealed record IncidentSummary(Guid Id, Guid OrganizationId, string ReferenceNumber, string Title, string Location,
    PoliceOccasion Occasion, IncidentStatus Status, DateTimeOffset CreatedAt, int AssignedResourceCount);
public sealed record IncidentDetails(Guid Id, Guid OrganizationId, string ReferenceNumber, string Title, string Description,
    string Location, PoliceOccasion Occasion, IncidentStatus Status, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    IReadOnlyList<StatusHistoryItem> StatusHistory, IReadOnlyList<AssignedResource> AssignedResources);
public sealed record StatusHistoryItem(IncidentStatus Status, DateTimeOffset ChangedAt, string ChangedBy);
public sealed record AssignedResource(Guid Id, string CallSign, string Name, ResourceStatus Status, DateTimeOffset AssignedAt);
