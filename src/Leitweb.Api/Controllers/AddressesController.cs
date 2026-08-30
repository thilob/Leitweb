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
        var normalized = Normalize(query);
        if (normalized.Length < 2) return Ok(Array.Empty<object>());
        limit = Math.Clamp(limit, 1, 100);
        var candidates = _db.Addresses.AsNoTracking().AsQueryable();
        foreach (var searchToken in normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var token = searchToken;
            candidates = candidates.Where(x =>
                (x.Street + " " + x.HouseNumber + " " + x.PostalCode + " " + x.Municipality).ToLower().Contains(token));
        }

        // Fetch a bounded candidate set from PostgreSQL and rank exact, separator-independent matches in memory.
        // This avoids empty fields (for example a missing postal code) introducing double spaces that break a full-text match.
        var matches = await candidates.OrderBy(x => x.Municipality).ThenBy(x => x.Street).ThenBy(x => x.HouseNumber)
            .Take(500).ToListAsync(ct);
        return Ok(matches
            .OrderByDescending(x => Normalize($"{x.Street} {x.HouseNumber} {x.PostalCode} {x.Municipality}") == normalized)
            .ThenByDescending(x => Normalize($"{x.Street} {x.HouseNumber} {x.Municipality}") == normalized)
            .ThenBy(x => x.HouseNumber.Length)
            .ThenBy(x => x.HouseNumber)
            .Take(limit)
            .Select(x => new { x.Id, x.Municipality, x.PostalCode, x.Street, x.HouseNumber, x.Latitude, x.Longitude,
                DisplayName = x.Street + " " + x.HouseNumber + ", " + (x.PostalCode == "" ? "" : x.PostalCode + " ") + x.Municipality }));
    }

    private static string Normalize(string? value) => string.Join(' ', (value ?? string.Empty)
        .Replace(',', ' ').Replace(';', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
}
