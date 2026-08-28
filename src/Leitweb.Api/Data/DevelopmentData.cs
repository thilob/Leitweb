using Leitweb.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Leitweb.Api.Data;

public static class DevelopmentData
{
    public static readonly Guid ExampleOrganizationId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static async Task SeedAsync(LeitwebDbContext db)
    {
        if (await db.Incidents.AnyAsync()) return;
        var callSigns = new[] { "Well 11/21", "Well 11/31", "Well 11/32", "Well 11/33", "Well 11/34",
            "Well 11/35", "Well 11/36", "Well 11/37", "Well 11/38", "Well 11/81" };
        var resources = callSigns.Select((callSign, index) => new OperationalResource
        {
            OrganizationId = ExampleOrganizationId, CallSign = callSign,
            Name = index == 0 ? "Dienstgruppenleitung" : index == callSigns.Length - 1 ? "Verkehrsunfallaufnahme" : "Streifenwagen",
            Status = index < 6 ? ResourceStatus.Available : index < 9 ? ResourceStatus.Dispatched : ResourceStatus.Unavailable
        }).ToArray();
        var incident = new Incident
        {
            OrganizationId = ExampleOrganizationId, ReferenceNumber = "E-2026-0001",
            Title = "Verdächtige Wahrnehmung", Occasion = PoliceOccasion.SuspiciousPerson,
            Description = "Eine Anwohnerin meldet eine verdächtige Person im Bereich des Marktplatzes.", Location = "Marktplatz 1, Well"
        };
        incident.StatusHistory.Add(new IncidentStatusEntry { Status = IncidentStatus.Open, ChangedBy = "system" });
        db.AddRange(resources); db.Add(incident);
        await db.SaveChangesAsync();
    }
}
