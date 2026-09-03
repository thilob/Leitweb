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
        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var hasHouseNumber = tokens.Length > 1 && tokens[^1].Any(char.IsDigit);
        var streetPrefix = hasHouseNumber ? string.Join(' ', tokens[..^1]) : normalized;
        var houseNumberPrefix = hasHouseNumber ? tokens[^1] : null;
        var candidates = _db.Addresses.AsNoTracking()
            .Where(x => x.Street.ToLower().StartsWith(streetPrefix));
        if (houseNumberPrefix is not null)
            candidates = candidates.Where(x => x.HouseNumber.ToLower().StartsWith(houseNumberPrefix));

        // Fetch a bounded, index-backed street prefix result and rank exact matches in memory.
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
