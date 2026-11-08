namespace Leitweb.Api.Security;

public static class Permissions
{
    public const string UserAdminRole = "user-admin";
    public const string UserAdminPolicy = "user-admin";
    public const string GisViewRole = "gis-sehen";
    public const string GisEditRole = "gis-objekte-aendern";
    public const string GisFullAccessRole = "gis-vollzugriff";
    public const string GisViewPolicy = "gis-sehen";
    public const string GisEditPolicy = "gis-objekte-aendern";
    public const string GisFullAccessPolicy = "gis-vollzugriff";
    public static readonly string[] GisRoles = { GisViewRole, GisEditRole, GisFullAccessRole };
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
