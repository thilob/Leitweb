using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;
using Leitweb.Api.Data;
using Leitweb.Api.Domain;
using Leitweb.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.IO;

namespace Leitweb.Api.Controllers;

[ApiController, Route("api/v1/gis")]
public sealed class GisController : ControllerBase
{
    private readonly LeitwebDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private static readonly GeoJsonReader GeoJsonReader = new();
    private static readonly GeoJsonWriter GeoJsonWriter = new();
    public GisController(LeitwebDbContext db, IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    [HttpGet("layers"), Authorize(Policy = Permissions.GisViewPolicy)]
    public async Task<IActionResult> GetLayers([FromQuery] Guid organizationId, CancellationToken ct) => Ok(await _db.GisLayers
        .AsNoTracking().Where(x => x.OrganizationId == organizationId).OrderBy(x => x.Name)
        .Select(x => new { x.Id, x.OrganizationId, x.Name, x.Description, x.Color, x.IsEditable }).ToListAsync(ct));

    [HttpPost("layers"), Authorize(Policy = Permissions.GisFullAccessPolicy)]
    public async Task<IActionResult> CreateLayer(CreateGisLayer request, CancellationToken ct)
    {
        if (request.OrganizationId == Guid.Empty || string.IsNullOrWhiteSpace(request.Name)) return ValidationProblem("Organisation und Layername sind erforderlich.");
        if (!IsColor(request.Color)) return ValidationProblem("Die Farbe muss als hexadezimaler RGB-Wert angegeben werden.");
        var layer = new GisLayer { OrganizationId = request.OrganizationId, Name = request.Name.Trim(), Description = request.Description?.Trim() ?? string.Empty, Color = request.Color, IsEditable = request.IsEditable };
        _db.GisLayers.Add(layer); await _db.SaveChangesAsync(ct);
        return Created($"/api/v1/gis/layers/{layer.Id}", new { layer.Id });
    }

    [HttpGet("features"), Authorize(Policy = Permissions.GisViewPolicy)]
    public async Task<IActionResult> GetFeatures([FromQuery] Guid organizationId, [FromQuery] Guid? layerId, CancellationToken ct)
    {
        var query = _db.GisFeatures.AsNoTracking().Where(x => x.OrganizationId == organizationId);
        if (layerId.HasValue) query = query.Where(x => x.GisLayerId == layerId);
        var features = await query.Take(5000).ToListAsync(ct);
        var collection = new JsonObject { ["type"] = "FeatureCollection", ["features"] = new JsonArray(features.Select(ToGeoJson).ToArray()) };
        return Content(collection.ToJsonString(), "application/geo+json");
    }

    [HttpPost("features"), Authorize(Policy = Permissions.GisEditPolicy)]
    public async Task<IActionResult> CreateFeature(SaveGisFeature request, CancellationToken ct)
    {
        var layer = await _db.GisLayers.FindAsync(new object[] { request.LayerId }, ct);
        if (layer is null || layer.OrganizationId != request.OrganizationId || !layer.IsEditable) return BadRequest("Der Layer ist nicht editierbar.");
        var geometry = ReadGeometry(request.Geometry);
        if (geometry is null) return ValidationProblem("Die GeoJSON-Geometrie ist ungültig.");
        var feature = new GisFeature { OrganizationId = request.OrganizationId, GisLayerId = request.LayerId, Name = request.Name?.Trim() ?? string.Empty,
            Description = request.Description?.Trim() ?? string.Empty, Geometry = geometry, PropertiesJson = NormalizeProperties(request.Properties), UpdatedBy = Subject() };
        _db.GisFeatures.Add(feature); await _db.SaveChangesAsync(ct);
        return Created($"/api/v1/gis/features/{feature.Id}", new { feature.Id });
    }

    [HttpPut("features/{id:guid}"), Authorize(Policy = Permissions.GisEditPolicy)]
    public async Task<IActionResult> UpdateFeature(Guid id, SaveGisFeature request, CancellationToken ct)
    {
        var feature = await _db.GisFeatures.Include(x => x.Layer).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (feature is null) return NotFound();
        if (feature.OrganizationId != request.OrganizationId || feature.GisLayerId != request.LayerId || !feature.Layer.IsEditable) return BadRequest("Der Layer ist nicht editierbar.");
        var geometry = ReadGeometry(request.Geometry);
        if (geometry is null) return ValidationProblem("Die GeoJSON-Geometrie ist ungültig.");
        feature.Name = request.Name?.Trim() ?? string.Empty; feature.Description = request.Description?.Trim() ?? string.Empty;
        feature.Geometry = geometry; feature.PropertiesJson = NormalizeProperties(request.Properties); feature.UpdatedBy = Subject(); feature.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct); return NoContent();
    }

    [HttpDelete("features/{id:guid}"), Authorize(Policy = Permissions.GisEditPolicy)]
    public async Task<IActionResult> DeleteFeature(Guid id, [FromQuery] Guid organizationId, CancellationToken ct)
    {
        var feature = await _db.GisFeatures.Include(x => x.Layer).SingleOrDefaultAsync(x => x.Id == id && x.OrganizationId == organizationId, ct);
        if (feature is null) return NotFound(); if (!feature.Layer.IsEditable) return BadRequest("Der Layer ist nicht editierbar.");
        _db.GisFeatures.Remove(feature); await _db.SaveChangesAsync(ct); return NoContent();
    }

    [HttpGet("sources"), Authorize(Policy = Permissions.GisViewPolicy)]
    public async Task<IActionResult> GetSources([FromQuery] Guid organizationId, CancellationToken ct) => Ok(await _db.GisSources.AsNoTracking()
        .Where(x => x.OrganizationId == organizationId && x.Enabled).OrderBy(x => x.Name).ToListAsync(ct));

    [HttpGet("qgis-layers"), Authorize(Policy = Permissions.GisViewPolicy)]
    public async Task<IActionResult> GetQgisLayers(CancellationToken ct)
    {
        var serverUrl = _configuration["Gis:QgisServerUrl"];
        var publicUrl = _configuration["Gis:QgisPublicUrl"];
        if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out var serverUri)
            || !Uri.TryCreate(publicUrl, UriKind.Absolute, out var publicUri)) return Ok(Array.Empty<object>());

        try
        {
            var separator = string.IsNullOrEmpty(serverUri.Query) ? "?" : "&";
            using var response = await _httpClientFactory.CreateClient().GetAsync(
                serverUri + separator + "SERVICE=WMS&REQUEST=GetCapabilities", ct);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            var document = XDocument.Load(reader, LoadOptions.None);
            var layers = document.Descendants().Where(x => x.Name.LocalName == "Layer")
                .Select(x => new
                {
                    LayerName = x.Elements().FirstOrDefault(e => e.Name.LocalName == "Name")?.Value.Trim(),
                    Title = x.Elements().FirstOrDefault(e => e.Name.LocalName == "Title")?.Value.Trim()
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.LayerName))
                .GroupBy(x => x.LayerName!, StringComparer.Ordinal)
                .Select(group => group.First())
                .Select(x => new { Id = "qgis:" + x.LayerName, Name = string.IsNullOrWhiteSpace(x.Title) ? x.LayerName : x.Title,
                    ServiceType = "WMS", ServiceUrl = publicUri.ToString(), x.LayerName })
                .ToList();
            return Ok(layers);
        }
        catch (Exception exception) when (exception is HttpRequestException or XmlException)
        {
            return Problem("Die Layer des QGIS-Projekts konnten nicht gelesen werden.", statusCode: StatusCodes.Status502BadGateway);
        }
    }

    [HttpPost("sources"), Authorize(Policy = Permissions.GisFullAccessPolicy)]
    public async Task<IActionResult> CreateSource(CreateGisSource request, CancellationToken ct)
    {
        if (!Uri.TryCreate(request.ServiceUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return ValidationProblem("Externe OGC-Dienste müssen eine absolute HTTPS-Adresse verwenden.");
        var type = request.ServiceType.Trim().ToUpperInvariant();
        if (type is not ("WMS" or "WFS")) return ValidationProblem("Unterstützt werden WMS und WFS.");
        var source = new GisSource { OrganizationId = request.OrganizationId, Name = request.Name.Trim(), ServiceType = type,
            ServiceUrl = uri.ToString(), LayerName = request.LayerName.Trim() };
        _db.GisSources.Add(source); await _db.SaveChangesAsync(ct); return Created($"/api/v1/gis/sources/{source.Id}", new { source.Id });
    }

    [HttpGet("profiles"), Authorize(Policy = Permissions.GisViewPolicy)]
    public async Task<IActionResult> GetProfiles([FromQuery] Guid organizationId, CancellationToken ct) => Ok(await _db.GisMapProfiles.AsNoTracking()
        .Where(x => x.OrganizationId == organizationId && x.UserSubject == Subject()).OrderByDescending(x => x.IsDefault).ThenBy(x => x.Name).ToListAsync(ct));

    [HttpPost("profiles"), Authorize(Policy = Permissions.GisViewPolicy)]
    public async Task<IActionResult> SaveProfile(SaveGisProfile request, CancellationToken ct)
    {
        try { using var _ = JsonDocument.Parse(request.ConfigurationJson); } catch (JsonException) { return ValidationProblem("Die Kartenkonfiguration ist kein gültiges JSON."); }
        var subject = Subject();
        var profile = request.Id.HasValue ? await _db.GisMapProfiles.SingleOrDefaultAsync(x => x.Id == request.Id && x.UserSubject == subject, ct) : null;
        if (request.Id.HasValue && profile is null) return NotFound();
        profile ??= new GisMapProfile { OrganizationId = request.OrganizationId, UserSubject = subject };
        profile.Name = request.Name.Trim(); profile.ConfigurationJson = request.ConfigurationJson; profile.IsDefault = request.IsDefault; profile.UpdatedAt = DateTimeOffset.UtcNow;
        if (profile.IsDefault)
        {
            var defaults = await _db.GisMapProfiles.Where(x => x.OrganizationId == request.OrganizationId && x.UserSubject == subject && x.IsDefault).ToListAsync(ct);
            foreach (var existing in defaults) existing.IsDefault = false;
        }
        if (profile.Id == default) _db.GisMapProfiles.Add(profile); await _db.SaveChangesAsync(ct); return Ok(new { profile.Id });
    }

    private string Subject() => User.FindFirstValue("sub") ?? User.Identity?.Name ?? "unknown";
    private static bool IsColor(string value) => value.Length == 7 && value[0] == '#' && value.Skip(1).All(Uri.IsHexDigit);
    private static NetTopologySuite.Geometries.Geometry? ReadGeometry(JsonElement value) { try { var geometry = GeoJsonReader.Read<NetTopologySuite.Geometries.Geometry>(value.GetRawText()); geometry.SRID = 4326; return geometry; } catch { return null; } }
    private static string NormalizeProperties(JsonElement value) => value.ValueKind == JsonValueKind.Object ? value.GetRawText() : "{}";
    private static JsonNode ToGeoJson(GisFeature feature) => new JsonObject { ["type"] = "Feature", ["id"] = feature.Id.ToString(),
        ["geometry"] = JsonNode.Parse(GeoJsonWriter.Write(feature.Geometry)), ["properties"] = new JsonObject { ["id"] = feature.Id.ToString(), ["layerId"] = feature.GisLayerId.ToString(),
            ["name"] = feature.Name, ["description"] = feature.Description, ["attributes"] = JsonNode.Parse(feature.PropertiesJson), ["updatedAt"] = feature.UpdatedAt } };
}

public sealed record CreateGisLayer(Guid OrganizationId, string Name, string? Description, string Color, bool IsEditable);
public sealed record SaveGisFeature(Guid OrganizationId, Guid LayerId, string? Name, string? Description, JsonElement Geometry, JsonElement Properties);
public sealed record CreateGisSource(Guid OrganizationId, string Name, string ServiceType, string ServiceUrl, string LayerName);
public sealed record SaveGisProfile(Guid? Id, Guid OrganizationId, string Name, string ConfigurationJson, bool IsDefault);
