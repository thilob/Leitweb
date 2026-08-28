using Leitweb.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Leitweb.Api.Data;

public static class DevelopmentData
{
    public static readonly Guid ExampleOrganizationId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static async Task SeedAsync(LeitwebDbContext db)
    {
        if (await db.Incidents.AnyAsync()) return;
        var resource = new OperationalResource
        {
            OrganizationId = ExampleOrganizationId, CallSign = "Well 1/10", Name = "Streifenwagen"
        };
        var incident = new Incident
        {
            OrganizationId = ExampleOrganizationId, ReferenceNumber = "E-2026-0001",
            Title = "Verdächtige Wahrnehmung", Occasion = PoliceOccasion.SuspiciousPerson,
            Description = "Eine Anwohnerin meldet eine verdächtige Person im Bereich des Marktplatzes.", Location = "Marktplatz 1, Well"
        };
        incident.StatusHistory.Add(new IncidentStatusEntry { Status = IncidentStatus.Open, ChangedBy = "system" });
        db.AddRange(resource, incident);
        await db.SaveChangesAsync();
    }
}
