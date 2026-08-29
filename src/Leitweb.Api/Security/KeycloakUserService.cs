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
        var admin = await GetAdminContextAsync(ct);
        if (admin is null) return CreateKeycloakUserResult.NotConfigured;

        using var request = admin.Request(HttpMethod.Post, "users");
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
        if (!response.IsSuccessStatusCode) return CreateKeycloakUserResult.KeycloakRejected;

        var userUrl = response.Headers.Location?.ToString();
        if (string.IsNullOrWhiteSpace(userUrl)) return CreateKeycloakUserResult.KeycloakRejected;
        if (userUrl.StartsWith('/')) userUrl = admin.BaseUrl + userUrl;
        using var verifyRequest = new HttpRequestMessage(HttpMethod.Get, userUrl);
        verifyRequest.Headers.Authorization = request.Headers.Authorization;
        using var verifyResponse = await _http.SendAsync(verifyRequest, ct);
        if (!verifyResponse.IsSuccessStatusCode) return CreateKeycloakUserResult.KeycloakRejected;
        var createdUser = await verifyResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        var permissionsStored = createdUser.TryGetProperty("attributes", out var attributes)
            && attributes.TryGetProperty("permissions", out var permissions)
            && permissions.ValueKind == JsonValueKind.Array
            && Permissions.All.All(expected => permissions.EnumerateArray().Any(value => value.GetString() == expected));
        if (permissionsStored) return CreateKeycloakUserResult.Created;

        using var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, userUrl);
        deleteRequest.Headers.Authorization = request.Headers.Authorization;
        using var deleteResponse = await _http.SendAsync(deleteRequest, ct);
        return CreateKeycloakUserResult.UserProfileRejectedPermissions;
    }

    public async Task<IReadOnlyList<KeycloakUserSummary>?> GetUsersAsync(CancellationToken ct)
    {
        var admin = await GetAdminContextAsync(ct);
        if (admin is null) return null;
        using var request = admin.Request(HttpMethod.Get, "users?max=500");
        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return Array.Empty<KeycloakUserSummary>();
        var users = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        var result = new List<KeycloakUserSummary>();
        foreach (var user in users.EnumerateArray())
        {
            var id = user.GetProperty("id").GetString()!;
            var roles = await GetAssignedGisRolesAsync(admin, id, ct);
            result.Add(new KeycloakUserSummary(id, Value(user, "username"), Value(user, "firstName"),
                Value(user, "lastName"), Value(user, "email"), roles));
        }
        return result.OrderBy(x => x.Username, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task<UpdateGisRolesResult> SetGisRoleAsync(string userId, string? role, CancellationToken ct)
    {
        if (role is not null && !Permissions.GisRoles.Contains(role, StringComparer.Ordinal))
            return UpdateGisRolesResult.InvalidRole;
        var admin = await GetAdminContextAsync(ct);
        if (admin is null) return UpdateGisRolesResult.NotConfigured;

        var representations = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var roleName in Permissions.GisRoles)
        {
            using var roleRequest = admin.Request(HttpMethod.Get, $"roles/{Uri.EscapeDataString(roleName)}");
            using var roleResponse = await _http.SendAsync(roleRequest, ct);
            if (!roleResponse.IsSuccessStatusCode) return UpdateGisRolesResult.RolesNotConfigured;
            representations[roleName] = (await roleResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct)).Clone();
        }

        var assigned = await GetAssignedGisRolesAsync(admin, userId, ct);
        var remove = assigned.Where(x => x != role).Select(x => representations[x]).ToArray();
        if (remove.Length > 0)
        {
            using var removeRequest = admin.Request(HttpMethod.Delete, $"users/{Uri.EscapeDataString(userId)}/role-mappings/realm");
            removeRequest.Content = JsonContent.Create(remove);
            using var removeResponse = await _http.SendAsync(removeRequest, ct);
            if (!removeResponse.IsSuccessStatusCode) return removeResponse.StatusCode == HttpStatusCode.NotFound
                ? UpdateGisRolesResult.UserNotFound : UpdateGisRolesResult.KeycloakRejected;
        }
        if (role is not null && !assigned.Contains(role, StringComparer.Ordinal))
        {
            using var addRequest = admin.Request(HttpMethod.Post, $"users/{Uri.EscapeDataString(userId)}/role-mappings/realm");
            addRequest.Content = JsonContent.Create(new[] { representations[role] });
            using var addResponse = await _http.SendAsync(addRequest, ct);
            if (!addResponse.IsSuccessStatusCode) return addResponse.StatusCode == HttpStatusCode.NotFound
                ? UpdateGisRolesResult.UserNotFound : UpdateGisRolesResult.KeycloakRejected;
        }
        return UpdateGisRolesResult.Updated;
    }

    private async Task<string[]> GetAssignedGisRolesAsync(AdminContext admin, string userId, CancellationToken ct)
    {
        using var request = admin.Request(HttpMethod.Get, $"users/{Uri.EscapeDataString(userId)}/role-mappings/realm");
        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return Array.Empty<string>();
        var roles = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        return roles.EnumerateArray().Select(x => Value(x, "name"))
            .Where(x => Permissions.GisRoles.Contains(x, StringComparer.Ordinal)).ToArray();
    }

    private async Task<AdminContext?> GetAdminContextAsync(CancellationToken ct)
    {
        var baseUrl = (_configuration["KeycloakAdmin:BaseUrl"] ?? "http://identity:8080").TrimEnd('/');
        var realm = _configuration["KeycloakAdmin:Realm"] ?? "leitweb";
        var clientId = _configuration["KeycloakAdmin:ClientId"] ?? "leitweb-user-admin";
        var clientSecret = _configuration["KeycloakAdmin:ClientSecret"];
        if (string.IsNullOrWhiteSpace(clientSecret)) return null;
        using var response = await _http.PostAsync($"{baseUrl}/realms/{Uri.EscapeDataString(realm)}/protocol/openid-connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "client_credentials", ["client_id"] = clientId, ["client_secret"] = clientSecret }), ct);
        if (!response.IsSuccessStatusCode) return null;
        var token = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        return token.TryGetProperty("access_token", out var accessToken)
            ? new AdminContext(baseUrl, realm, accessToken.GetString()!) : null;
    }

    private static string Value(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value.GetString() ?? string.Empty : string.Empty;

    private sealed record AdminContext(string BaseUrl, string Realm, string AccessToken)
    {
        public HttpRequestMessage Request(HttpMethod method, string path)
        {
            var request = new HttpRequestMessage(method, $"{BaseUrl}/admin/realms/{Uri.EscapeDataString(Realm)}/{path}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
            return request;
        }
    }
}

public sealed record NewKeycloakUser(string Username, string FirstName, string LastName, string? Email, string TemporaryPassword);
public sealed record KeycloakUserSummary(string Id, string Username, string FirstName, string LastName, string Email, IReadOnlyList<string> GisRoles);
public enum CreateKeycloakUserResult { Created, AlreadyExists, NotConfigured, KeycloakRejected, UserProfileRejectedPermissions }
public enum UpdateGisRolesResult { Updated, InvalidRole, UserNotFound, NotConfigured, RolesNotConfigured, KeycloakRejected }
