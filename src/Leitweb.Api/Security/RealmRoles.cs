using System.Security.Claims;
using System.Text.Json;

namespace Leitweb.Api.Security;

public static class RealmRoles
{
    public static bool HasRole(ClaimsPrincipal user, string role)
    {
        var realmAccess = user.FindFirst("realm_access")?.Value;
        if (string.IsNullOrWhiteSpace(realmAccess)) return false;
        try
        {
            using var document = JsonDocument.Parse(realmAccess);
            return document.RootElement.TryGetProperty("roles", out var roles)
                && roles.ValueKind == JsonValueKind.Array
                && roles.EnumerateArray().Any(item => string.Equals(item.GetString(), role, StringComparison.Ordinal));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static bool HasAnyRole(ClaimsPrincipal user, params string[] roles) => roles.Any(role => HasRole(user, role));
}
