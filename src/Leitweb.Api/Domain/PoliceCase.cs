namespace Leitweb.Api.Domain;

public sealed class PoliceCase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid IncidentId { get; set; }
    public Incident Incident { get; set; } = null!;
    public string FileNumber { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public CaseStatus Status { get; set; } = CaseStatus.Open;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<CasePerson> Persons { get; set; } = new();
    public List<EvidenceItem> EvidenceItems { get; set; } = new();
    public List<CaseDocument> Documents { get; set; } = new();
}

public enum CaseStatus { Open, UnderInvestigation, Submitted, Closed }
public enum PersonRole { Accused, Suspect, Victim, Witness, ReportingPerson, InjuredPerson, Guardian, Other }
public enum EvidenceStatus { Seized, Secured, InStorage, SentForExamination, Released, Destroyed }
public enum DocumentType { ShortReport, CriminalComplaint, IncidentReport, WitnessStatement, SeizureRecord, CoverLetter, ClosingReport, Other }
public enum DispatchStatus { Draft, Dispatched, Acknowledged, Returned }

public sealed class CasePerson
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PoliceCaseId { get; set; }
    public PoliceCase PoliceCase { get; set; } = null!;
    public PersonRole Role { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public DateOnly? DateOfBirth { get; set; }
    public string Address { get; set; } = string.Empty;
    public string Contact { get; set; } = string.Empty;
}

public sealed class EvidenceItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PoliceCaseId { get; set; }
    public PoliceCase PoliceCase { get; set; } = null!;
    public string EvidenceNumber { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string StorageLocation { get; set; } = string.Empty;
    public EvidenceStatus Status { get; set; } = EvidenceStatus.Secured;
    public DateTimeOffset SecuredAt { get; set; } = DateTimeOffset.UtcNow;
    public string SecuredBy { get; set; } = string.Empty;
}

public sealed class CaseDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PoliceCaseId { get; set; }
    public PoliceCase PoliceCase { get; set; } = null!;
    public DocumentType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string CreatedBy { get; set; } = string.Empty;
    public List<DocumentDispatch> Dispatches { get; set; } = new();
}

public sealed class DocumentDispatch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CaseDocumentId { get; set; }
    public CaseDocument Document { get; set; } = null!;
    public string Recipient { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public DispatchStatus Status { get; set; } = DispatchStatus.Dispatched;
    public DateTimeOffset DispatchedAt { get; set; } = DateTimeOffset.UtcNow;
    public string DispatchedBy { get; set; } = string.Empty;
}
