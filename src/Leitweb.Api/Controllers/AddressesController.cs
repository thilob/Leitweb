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
        limit = Math.Clamp(limit, 1, 100);
        var addressParts = query.Split([',', ';'], 2);
        var streetAndNumber = Normalize(addressParts[0]);
        if (streetAndNumber.Length < 2) return Ok(Array.Empty<object>());
        var locality = addressParts.Length == 2 ? Normalize(addressParts[1]) : string.Empty;
        var tokens = streetAndNumber.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var hasHouseNumber = tokens.Length > 1 && tokens[^1].Any(char.IsDigit);
        var streetPrefix = hasHouseNumber ? string.Join(' ', tokens[..^1]) : streetAndNumber;
        var houseNumberPrefix = hasHouseNumber ? tokens[^1] : null;
        var candidates = _db.Addresses.AsNoTracking();
        if (locality.Length > 0 && houseNumberPrefix is not null)
        {
            // A complete address must not resolve to a neighboring house number.
            candidates = candidates.Where(x => x.Street.ToLower() == streetPrefix &&
                x.HouseNumber.ToLower() == houseNumberPrefix);
        }
        else
        {
            candidates = candidates.Where(x => x.Street.ToLower().StartsWith(streetPrefix));
            if (houseNumberPrefix is not null)
                candidates = candidates.Where(x => x.HouseNumber.ToLower().StartsWith(houseNumberPrefix));
        }
        if (locality.Length > 0)
        {
            candidates = candidates.Where(x => x.Municipality.ToLower() == locality ||
                (x.PostalCode + " " + x.Municipality.ToLower()) == locality);
        }

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
