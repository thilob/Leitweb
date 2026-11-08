using Leitweb.Api.Data;
using Leitweb.Api.Domain;
using Leitweb.Api.Realtime;
using Leitweb.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Leitweb.Api.Controllers;

[ApiController, Route("api/v1/resources")]
public sealed class ResourcesController : ControllerBase
{
    private readonly LeitwebDbContext _db;
    private readonly LiveUpdateHub _updates;
    public ResourcesController(LeitwebDbContext db, LiveUpdateHub updates) { _db = db; _updates = updates; }

    [HttpGet, Authorize(Policy = Permissions.ResourceRead)]
    public async Task<ActionResult<IReadOnlyList<ResourceDto>>> GetAll([FromQuery] Guid organizationId, CancellationToken ct) =>
        await _db.Resources.AsNoTracking().Where(x => x.OrganizationId == organizationId).OrderBy(x => x.CallSign)
            .Select(x => new ResourceDto(x.Id, x.OrganizationId, x.CallSign, x.Name, x.Status)).ToListAsync(ct);

    [HttpPost, Authorize(Policy = Permissions.ResourceManage)]
    public async Task<ActionResult<ResourceDto>> Create(CreateResource request, CancellationToken ct)
    {
        if (request.OrganizationId == Guid.Empty || string.IsNullOrWhiteSpace(request.CallSign) || string.IsNullOrWhiteSpace(request.Name))
            return ValidationProblem("Organisation, Funkrufname und Bezeichnung sind erforderlich.");
        var resource = new OperationalResource { OrganizationId = request.OrganizationId, CallSign = request.CallSign.Trim(), Name = request.Name.Trim() };
        _db.Resources.Add(resource); await _db.SaveChangesAsync(ct);
        await _updates.BroadcastAsync("resources.changed");
        return Created($"/api/v1/resources/{resource.Id}", new ResourceDto(resource.Id, resource.OrganizationId, resource.CallSign, resource.Name, resource.Status));
    }

    [HttpPut("{id:guid}"), Authorize(Policy = Permissions.ResourceManage)]
    public async Task<IActionResult> Update(Guid id, UpdateResource request, CancellationToken ct)
    {
        var resource = await _db.Resources.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (resource is null) return NotFound();
        resource.CallSign = request.CallSign.Trim(); resource.Name = request.Name.Trim(); resource.Status = request.Status;
        await _db.SaveChangesAsync(ct); await _updates.BroadcastAsync("resources.changed"); return NoContent();
    }
}

public sealed record CreateResource(Guid OrganizationId, string CallSign, string Name);
public sealed record UpdateResource(string CallSign, string Name, ResourceStatus Status);
public sealed record ResourceDto(Guid Id, Guid OrganizationId, string CallSign, string Name, ResourceStatus Status);
