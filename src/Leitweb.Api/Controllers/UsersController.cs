using Leitweb.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Leitweb.Api.Controllers;

[ApiController, Route("api/v1/users")]
public sealed class UsersController : ControllerBase
{
    private readonly KeycloakUserService _users;
    public UsersController(KeycloakUserService users) => _users = users;

    [HttpGet, Authorize(Policy = Permissions.UserAdminPolicy)]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var users = await _users.GetUsersAsync(ct);
        return users is null
            ? Problem("Der Keycloak-Service-Account ist noch nicht konfiguriert.", statusCode: 503)
            : Ok(users);
    }

    [HttpPost, Authorize(Policy = Permissions.UserAdminPolicy)]
    public async Task<IActionResult> Create(CreateUser request, CancellationToken ct)
    {
        var username = request.Username?.Trim() ?? string.Empty;
        if (username.Length < 3 || username.Length > 100 || username.Any(char.IsWhiteSpace))
            return ValidationProblem("Der Benutzername muss 3 bis 100 Zeichen lang sein und darf keine Leerzeichen enthalten.");
        if (string.IsNullOrWhiteSpace(request.FirstName) || string.IsNullOrWhiteSpace(request.LastName))
            return ValidationProblem("Vorname und Nachname sind erforderlich.");
        if (string.IsNullOrEmpty(request.TemporaryPassword) || request.TemporaryPassword.Length < 12)
            return ValidationProblem("Das temporäre Kennwort muss mindestens 12 Zeichen lang sein.");

        var result = await _users.CreateAsync(new NewKeycloakUser(username, request.FirstName.Trim(), request.LastName.Trim(),
            string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim(), request.TemporaryPassword), ct);
        return result switch
        {
            CreateKeycloakUserResult.Created => StatusCode(StatusCodes.Status201Created),
            CreateKeycloakUserResult.AlreadyExists => Conflict(new ProblemDetails { Title = "Benutzername ist bereits vergeben." }),
            CreateKeycloakUserResult.NotConfigured => Problem("Der Keycloak-Service-Account ist noch nicht konfiguriert.", statusCode: 503),
            CreateKeycloakUserResult.UserProfileRejectedPermissions => Problem("Keycloak hat das Attribut 'permissions' verworfen. Es muss im Realm unter Realm settings > User profile als mehrwertiges, nur durch Administratoren editierbares Attribut angelegt werden.", statusCode: 502),
            _ => Problem("Keycloak hat das Anlegen des Benutzers abgelehnt.", statusCode: 502)
        };
    }

    [HttpPut("{userId}/gis-role"), Authorize(Policy = Permissions.UserAdminPolicy)]
    public async Task<IActionResult> SetGisRole(string userId, SetGisRole request, CancellationToken ct)
    {
        var role = string.IsNullOrWhiteSpace(request.Role) ? null : request.Role.Trim();
        var result = await _users.SetGisRoleAsync(userId, role, ct);
        return result switch
        {
            UpdateGisRolesResult.Updated => NoContent(),
            UpdateGisRolesResult.InvalidRole => ValidationProblem("Die angegebene GIS-Rolle ist nicht zulässig."),
            UpdateGisRolesResult.UserNotFound => NotFound(),
            UpdateGisRolesResult.NotConfigured => Problem("Der Keycloak-Service-Account ist noch nicht konfiguriert.", statusCode: 503),
            UpdateGisRolesResult.RolesNotConfigured => Problem("Die GIS-Rollen sind im Keycloak-Realm noch nicht eingerichtet.", statusCode: 503),
            _ => Problem("Keycloak hat die Änderung der GIS-Rolle abgelehnt.", statusCode: 502)
        };
    }
}

public sealed record CreateUser(string Username, string FirstName, string LastName, string? Email, string TemporaryPassword);
public sealed record SetGisRole(string? Role);
