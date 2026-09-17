using System.Diagnostics;
using Leitweb.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Leitweb.Api.Diagnostics;

public sealed class RuntimeStatusService(
    LeitwebDbContext database,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    IWebHostEnvironment environment)
{
    public async Task<RuntimeStatusReport> CheckAsync(CancellationToken cancellationToken)
    {
        var checks = await Task.WhenAll(
            CheckDatabaseAsync(cancellationToken),
            CheckHttpAsync(
                "identity",
                "Keycloak / Anmeldung",
                GetIdentityMetadataUrl(),
                "OIDC-Konfiguration wurde aus dem Docker-Netz erreicht.",
                cancellationToken),
            CheckHttpAsync(
                "qgis",
                "QGIS-Kartendienst",
                AddQuery(configuration["Gis:QgisServerUrl"], "SERVICE=WMS&REQUEST=GetCapabilities"),
                "WMS-Capabilities wurden aus dem Docker-Netz erreicht.",
                cancellationToken));

        return new RuntimeStatusReport(
            checks.All(check => check.Status == "healthy") ? "healthy" : "degraded",
            DateTimeOffset.UtcNow,
            environment.EnvironmentName,
            checks,
            new RuntimePublicEndpoints(
                configuration["Authentication:PublicAuthority"] ?? configuration["Authentication:Authority"],
                configuration["Gis:QgisPublicUrl"]));
    }

    private async Task<RuntimeServiceStatus> CheckDatabaseAsync(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(6));
            var available = await database.Database.CanConnectAsync(timeout.Token);
            return new RuntimeServiceStatus(
                "database",
                "PostgreSQL / PostGIS",
                available ? "healthy" : "unhealthy",
                available ? "Datenbankverbindung und Anmeldung funktionieren." : "Der Datenbankserver hat die Verbindung abgelehnt oder antwortet nicht.",
                database.Database.ProviderName ?? "Datenbank",
                stopwatch.ElapsedMilliseconds,
                available ? null : "Containerzustand, Zugangsdaten und ConnectionStrings__Database prüfen.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return Failed("database", "PostgreSQL / PostGIS", database.Database.ProviderName ?? "Datenbank", stopwatch, exception,
                "Containerzustand, DNS-Name 'database', Port 5432 und Zugangsdaten prüfen.");
        }
    }

    private async Task<RuntimeServiceStatus> CheckHttpAsync(
        string id,
        string name,
        string? endpoint,
        string successMessage,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            return new RuntimeServiceStatus(id, name, "unhealthy", "Für diesen Dienst ist keine interne URL konfiguriert.", null, 0,
                id == "identity" ? "Authentication__MetadataAddress konfigurieren." : "Gis__QgisServerUrl konfigurieren.");

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(6));
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            using var response = await httpClientFactory.CreateClient().SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.IsSuccessStatusCode)
                return new RuntimeServiceStatus(id, name, "healthy", successMessage, endpoint, stopwatch.ElapsedMilliseconds, null);

            return new RuntimeServiceStatus(id, name, "unhealthy",
                $"Der Dienst antwortet mit HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).", endpoint, stopwatch.ElapsedMilliseconds,
                "Dienstprotokoll und konfigurierte URL prüfen.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            var hint = exception is TaskCanceledException
                ? "Zeitüberschreitung: Containerzustand, Netzwerk und Zielport prüfen."
                : "Docker-DNS, Dienstname, Zielport und Dienstprotokoll prüfen.";
            return Failed(id, name, endpoint, stopwatch, exception, hint);
        }
    }

    private string? GetIdentityMetadataUrl()
    {
        var configured = configuration["Authentication:MetadataAddress"];
        if (!string.IsNullOrWhiteSpace(configured)) return configured;
        var authority = configuration["Authentication:Authority"]?.TrimEnd('/');
        return authority is null ? null : $"{authority}/.well-known/openid-configuration";
    }

    private static string? AddQuery(string? endpoint, string query)
    {
        if (string.IsNullOrWhiteSpace(endpoint)) return null;
        return $"{endpoint}{(endpoint.Contains('?') ? '&' : '?')}{query}";
    }

    private static RuntimeServiceStatus Failed(string id, string name, string endpoint, Stopwatch stopwatch, Exception exception, string hint)
    {
        var cause = exception.GetBaseException();
        return new RuntimeServiceStatus(id, name, "unhealthy", $"{cause.GetType().Name}: {cause.Message}", endpoint,
            stopwatch.ElapsedMilliseconds, hint);
    }
}

public sealed record RuntimeStatusReport(
    string Status,
    DateTimeOffset CheckedAt,
    string Environment,
    IReadOnlyCollection<RuntimeServiceStatus> Services,
    RuntimePublicEndpoints PublicEndpoints);

public sealed record RuntimeServiceStatus(
    string Id,
    string Name,
    string Status,
    string Detail,
    string? Endpoint,
    long DurationMs,
    string? Hint);

public sealed record RuntimePublicEndpoints(string? Identity, string? Qgis);
