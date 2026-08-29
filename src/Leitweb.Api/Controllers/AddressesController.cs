using Leitweb.Api.Data;
using Leitweb.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Leitweb.Api.Controllers;

[ApiController, Route("api/v1/addresses")]
public sealed class AddressesController : ControllerBase
{
    private readonly LeitwebDbContext _db;
    public AddressesController(LeitwebDbContext db) => _db = db;

    [HttpGet("search"), Authorize(Policy = Permissions.IncidentRead)]
    public async Task<ActionResult> Search([FromQuery] string query, [FromQuery] int limit = 30, CancellationToken ct = default)
    {
        query = (query ?? string.Empty).Trim();
        if (query.Length < 2) return Ok(Array.Empty<object>());
        limit = Math.Clamp(limit, 1, 100);
        var normalized = string.Join(' ', query.Replace(',', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToLower();
        return Ok(await _db.Addresses.AsNoTracking()
            .Where(x => (x.Street + " " + x.HouseNumber + " " + x.PostalCode + " " + x.Municipality).ToLower().Contains(normalized))
            .OrderBy(x => x.Municipality).ThenBy(x => x.Street).ThenBy(x => x.HouseNumber).Take(limit)
            .Select(x => new { x.Id, x.Municipality, x.PostalCode, x.Street, x.HouseNumber, x.Latitude, x.Longitude,
                DisplayName = x.Street + " " + x.HouseNumber + ", " + (x.PostalCode == "" ? "" : x.PostalCode + " ") + x.Municipality }).ToListAsync(ct));
    }
}
