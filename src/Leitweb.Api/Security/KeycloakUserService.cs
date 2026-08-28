using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Leitweb.Api.Security;

public sealed class KeycloakUserService
{
    private readonly HttpClient _http;
    private readonly IConfiguration _configuration;

    public KeycloakUserService(HttpClient http, IConfiguration configuration)
    {
        _http = http;
        _configuration = configuration;
    }

    public async Task<CreateKeycloakUserResult> CreateAsync(NewKeycloakUser user, CancellationToken ct)
    {
        var baseUrl = (_configuration["KeycloakAdmin:BaseUrl"] ?? "http://identity:8080").TrimEnd('/');
        var realm = _configuration["KeycloakAdmin:Realm"] ?? "leitweb";
        var clientId = _configuration["KeycloakAdmin:ClientId"] ?? "leitweb-user-admin";
        var clientSecret = _configuration["KeycloakAdmin:ClientSecret"];
        if (string.IsNullOrWhiteSpace(clientSecret)) return CreateKeycloakUserResult.NotConfigured;

        using var tokenResponse = await _http.PostAsync($"{baseUrl}/realms/{Uri.EscapeDataString(realm)}/protocol/openid-connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret
            }), ct);
        if (!tokenResponse.IsSuccessStatusCode) return CreateKeycloakUserResult.KeycloakRejected;
        var token = await tokenResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        if (!token.TryGetProperty("access_token", out var accessToken)) return CreateKeycloakUserResult.KeycloakRejected;

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/admin/realms/{Uri.EscapeDataString(realm)}/users");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken.GetString());
        request.Content = JsonContent.Create(new
        {
            username = user.Username,
            email = user.Email,
            firstName = user.FirstName,
            lastName = user.LastName,
            enabled = true,
            emailVerified = false,
            credentials = new[] { new { type = "password", value = user.TemporaryPassword, temporary = true } },
            attributes = new Dictionary<string, string[]> { ["permissions"] = Permissions.All }
        });
        using var response = await _http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.Conflict) return CreateKeycloakUserResult.AlreadyExists;
        return response.IsSuccessStatusCode ? CreateKeycloakUserResult.Created : CreateKeycloakUserResult.KeycloakRejected;
    }
}

public sealed record NewKeycloakUser(string Username, string FirstName, string LastName, string? Email, string TemporaryPassword);
public enum CreateKeycloakUserResult { Created, AlreadyExists, NotConfigured, KeycloakRejected }
