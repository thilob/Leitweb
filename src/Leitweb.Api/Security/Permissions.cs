namespace Leitweb.Api.Security;

public static class Permissions
{
    public const string UserAdminRole = "user-admin";
    public const string UserAdminPolicy = "user-admin";
    public const string IncidentRead = "incident.read";
    public const string IncidentCreate = "incident.create";
    public const string IncidentUpdate = "incident.update";
    public const string ResourceRead = "resource.read";
    public const string ResourceManage = "resource.manage";
    public const string CaseRead = "case.read";
    public const string CaseManage = "case.manage";
    public const string DocumentDispatch = "document.dispatch";
    public static readonly string[] All = { IncidentRead, IncidentCreate, IncidentUpdate, ResourceRead, ResourceManage,
        CaseRead, CaseManage, DocumentDispatch };
}
