using Leitweb.Api.Data;
using Leitweb.Api.Domain;
using Leitweb.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Leitweb.Api.Controllers;

[ApiController, Route("api/v1/cases")]
public sealed class CasesController : ControllerBase
{
    private readonly LeitwebDbContext _db;
    public CasesController(LeitwebDbContext db) => _db = db;

    [HttpGet, Authorize(Policy = Permissions.CaseRead)]
    public async Task<ActionResult<IReadOnlyList<CaseSummary>>> GetAll([FromQuery] Guid organizationId, CancellationToken ct) =>
        await _db.Cases.AsNoTracking().Where(x => x.OrganizationId == organizationId).OrderByDescending(x => x.CreatedAt)
            .Select(x => new CaseSummary(x.Id, x.IncidentId, x.FileNumber, x.Subject, x.Status, x.CreatedAt,
                x.Persons.Count, x.EvidenceItems.Count, x.Documents.Count)).ToListAsync(ct);

    [HttpGet("{id:guid}"), Authorize(Policy = Permissions.CaseRead)]
    public async Task<ActionResult<CaseDetails>> Get(Guid id, CancellationToken ct)
    {
        var item = await _db.Cases.AsNoTracking().Where(x => x.Id == id).Select(x => new CaseDetails(
            x.Id, x.IncidentId, x.FileNumber, x.Subject, x.Status, x.CreatedAt,
            x.Persons.OrderBy(p => p.LastName).Select(p => new PersonDto(p.Id, p.Role, p.FirstName, p.LastName, p.DateOfBirth, p.Address, p.Contact)).ToList(),
            x.EvidenceItems.OrderBy(e => e.EvidenceNumber).Select(e => new EvidenceDto(e.Id, e.EvidenceNumber, e.Description, e.StorageLocation, e.Status, e.SecuredAt, e.SecuredBy)).ToList(),
            x.Documents.OrderByDescending(d => d.CreatedAt).Select(d => new DocumentDto(d.Id, d.Type, d.Title, d.Content, d.CreatedAt, d.CreatedBy,
                d.Dispatches.OrderByDescending(v => v.DispatchedAt).Select(v => new DispatchDto(v.Id, v.Recipient, v.Reference, v.Note, v.Status, v.DispatchedAt, v.DispatchedBy)).ToList())).ToList()
        )).SingleOrDefaultAsync(ct);
        return item is null ? NotFound() : item;
    }

    [HttpPost("from-incident/{incidentId:guid}"), Authorize(Policy = Permissions.CaseManage)]
    public async Task<ActionResult> CreateFromIncident(Guid incidentId, CreateCase request, CancellationToken ct)
    {
        var incident = await _db.Incidents.SingleOrDefaultAsync(x => x.Id == incidentId, ct);
        if (incident is null) return NotFound();
        var existing = await _db.Cases.AsNoTracking().SingleOrDefaultAsync(x => x.IncidentId == incidentId, ct);
        if (existing is not null) return Ok(new { existing.Id, alreadyExisted = true });
        var policeCase = new PoliceCase
        {
            OrganizationId = incident.OrganizationId, IncidentId = incidentId,
            FileNumber = request.FileNumber.Trim(), Subject = string.IsNullOrWhiteSpace(request.Subject) ? incident.Title : request.Subject.Trim()
        };
        _db.Cases.Add(policeCase); await _db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = policeCase.Id }, new { policeCase.Id });
    }

    [HttpPut("{id:guid}/status"), Authorize(Policy = Permissions.CaseManage)]
    public async Task<IActionResult> ChangeStatus(Guid id, ChangeCaseStatus request, CancellationToken ct)
    {
        var policeCase = await _db.Cases.FindAsync(new object[] { id }, ct); if (policeCase is null) return NotFound();
        policeCase.Status = request.Status; await _db.SaveChangesAsync(ct); return NoContent();
    }

    [HttpPost("{id:guid}/persons"), Authorize(Policy = Permissions.CaseManage)]
    public async Task<IActionResult> AddPerson(Guid id, AddPerson request, CancellationToken ct)
    {
        if (!await _db.Cases.AnyAsync(x => x.Id == id, ct)) return NotFound();
        _db.CasePersons.Add(new CasePerson { PoliceCaseId = id, Role = request.Role, FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(), DateOfBirth = request.DateOfBirth, Address = request.Address.Trim(), Contact = request.Contact.Trim() });
        await _db.SaveChangesAsync(ct); return NoContent();
    }

    [HttpPost("{id:guid}/evidence"), Authorize(Policy = Permissions.CaseManage)]
    public async Task<IActionResult> AddEvidence(Guid id, AddEvidence request, CancellationToken ct)
    {
        if (!await _db.Cases.AnyAsync(x => x.Id == id, ct)) return NotFound();
        _db.EvidenceItems.Add(new EvidenceItem { PoliceCaseId = id, EvidenceNumber = request.EvidenceNumber.Trim(),
            Description = request.Description.Trim(), StorageLocation = request.StorageLocation.Trim(), Status = request.Status,
            SecuredBy = User.Identity?.Name ?? "unknown" });
        await _db.SaveChangesAsync(ct); return NoContent();
    }

    [HttpPut("{id:guid}/evidence/{evidenceId:guid}/status"), Authorize(Policy = Permissions.CaseManage)]
    public async Task<IActionResult> ChangeEvidenceStatus(Guid id, Guid evidenceId, ChangeEvidenceStatus request, CancellationToken ct)
    {
        var evidence = await _db.EvidenceItems.SingleOrDefaultAsync(x => x.Id == evidenceId && x.PoliceCaseId == id, ct);
        if (evidence is null) return NotFound(); evidence.Status = request.Status; await _db.SaveChangesAsync(ct); return NoContent();
    }

    [HttpPost("{id:guid}/documents"), Authorize(Policy = Permissions.CaseManage)]
    public async Task<IActionResult> AddDocument(Guid id, AddDocument request, CancellationToken ct)
    {
        var policeCase = await _db.Cases.Include(x => x.Incident).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (policeCase is null) return NotFound();
        var content = string.IsNullOrWhiteSpace(request.Content) ? BuildTemplate(policeCase, request.Type) : request.Content.Trim();
        _db.CaseDocuments.Add(new CaseDocument { PoliceCaseId = id, Type = request.Type,
            Title = string.IsNullOrWhiteSpace(request.Title) ? DocumentName(request.Type) : request.Title.Trim(),
            Content = content, CreatedBy = User.Identity?.Name ?? "unknown" });
        await _db.SaveChangesAsync(ct); return NoContent();
    }

    [HttpPost("{id:guid}/documents/{documentId:guid}/dispatches"), Authorize(Policy = Permissions.DocumentDispatch)]
    public async Task<IActionResult> Dispatch(Guid id, Guid documentId, AddDispatch request, CancellationToken ct)
    {
        if (!await _db.CaseDocuments.AnyAsync(x => x.Id == documentId && x.PoliceCaseId == id, ct)) return NotFound();
        _db.DocumentDispatches.Add(new DocumentDispatch { CaseDocumentId = documentId, Recipient = request.Recipient.Trim(),
            Reference = request.Reference.Trim(), Note = request.Note.Trim(), DispatchedBy = User.Identity?.Name ?? "unknown" });
        await _db.SaveChangesAsync(ct); return NoContent();
    }

    private static string DocumentName(DocumentType type) => type switch
    {
        DocumentType.ShortReport => "Kurzbericht", DocumentType.CriminalComplaint => "Strafanzeige",
        DocumentType.IncidentReport => "Einsatzbericht", DocumentType.WitnessStatement => "Zeugenvernehmung",
        DocumentType.SeizureRecord => "Sicherstellungsprotokoll", DocumentType.CoverLetter => "Übersendungsschreiben",
        DocumentType.ClosingReport => "Abschlussbericht", _ => "Sonstiges Schreiben"
    };

    private static string BuildTemplate(PoliceCase c, DocumentType type) =>
        $"{DocumentName(type)}\n\nAktenzeichen: {c.FileNumber}\nBetreff: {c.Subject}\nEinsatzort: {c.Incident.Location}\n\nSachverhalt:\n{c.Incident.Description}\n\nMaßnahmen / Feststellungen:\n";
}

public sealed record CreateCase(string FileNumber, string Subject);
public sealed record ChangeCaseStatus(CaseStatus Status);
public sealed record AddPerson(PersonRole Role, string FirstName, string LastName, DateOnly? DateOfBirth, string Address, string Contact);
public sealed record AddEvidence(string EvidenceNumber, string Description, string StorageLocation, EvidenceStatus Status);
public sealed record ChangeEvidenceStatus(EvidenceStatus Status);
public sealed record AddDocument(DocumentType Type, string Title, string Content);
public sealed record AddDispatch(string Recipient, string Reference, string Note);
public sealed record CaseSummary(Guid Id, Guid IncidentId, string FileNumber, string Subject, CaseStatus Status, DateTimeOffset CreatedAt, int PersonCount, int EvidenceCount, int DocumentCount);
public sealed record CaseDetails(Guid Id, Guid IncidentId, string FileNumber, string Subject, CaseStatus Status, DateTimeOffset CreatedAt,
    IReadOnlyList<PersonDto> Persons, IReadOnlyList<EvidenceDto> Evidence, IReadOnlyList<DocumentDto> Documents);
public sealed record PersonDto(Guid Id, PersonRole Role, string FirstName, string LastName, DateOnly? DateOfBirth, string Address, string Contact);
public sealed record EvidenceDto(Guid Id, string EvidenceNumber, string Description, string StorageLocation, EvidenceStatus Status, DateTimeOffset SecuredAt, string SecuredBy);
public sealed record DocumentDto(Guid Id, DocumentType Type, string Title, string Content, DateTimeOffset CreatedAt, string CreatedBy, IReadOnlyList<DispatchDto> Dispatches);
public sealed record DispatchDto(Guid Id, string Recipient, string Reference, string Note, DispatchStatus Status, DateTimeOffset DispatchedAt, string DispatchedBy);
