namespace Leitweb.Api.Security;

public static class Permissions
{
    public const string IncidentRead = "incident.read";
    public const string IncidentCreate = "incident.create";
    public const string IncidentUpdate = "incident.update";
    public const string ResourceRead = "resource.read";
    public const string ResourceManage = "resource.manage";
    public static readonly string[] All = { IncidentRead, IncidentCreate, IncidentUpdate, ResourceRead, ResourceManage };
}
