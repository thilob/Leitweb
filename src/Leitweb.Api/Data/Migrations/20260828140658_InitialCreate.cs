using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Leitweb.Api.Data.Migrations
{
    public partial class InitialCreate : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:case_status", "open,under_investigation,submitted,closed")
                .Annotation("Npgsql:Enum:dispatch_status", "draft,dispatched,acknowledged,returned")
                .Annotation("Npgsql:Enum:document_type", "short_report,criminal_complaint,incident_report,witness_statement,seizure_record,cover_letter,closing_report,other")
                .Annotation("Npgsql:Enum:evidence_status", "seized,secured,in_storage,sent_for_examination,released,destroyed")
                .Annotation("Npgsql:Enum:incident_status", "open,dispatched,in_progress,closed,cancelled")
                .Annotation("Npgsql:Enum:person_role", "accused,suspect,victim,witness,reporting_person,injured_person,guardian,other")
                .Annotation("Npgsql:Enum:police_occasion", "other,traffic_accident,disturbance,theft,burglary,assault,domestic_violence,missing_person,suspicious_person,property_damage,traffic_control,administrative_assistance")
                .Annotation("Npgsql:Enum:resource_status", "unavailable,available,dispatched,on_scene");

            migrationBuilder.CreateTable(
                name: "address_register",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Municipality = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    PostalCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Street = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    HouseNumber = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Latitude = table.Column<double>(type: "double precision", nullable: true),
                    Longitude = table.Column<double>(type: "double precision", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_address_register", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "incidents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReferenceNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    Location = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Occasion = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_incidents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "operational_resources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CallSign = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operational_resources", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "incident_status_history",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IncidentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ChangedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_incident_status_history", x => x.Id);
                    table.ForeignKey(
                        name: "FK_incident_status_history_incidents_IncidentId",
                        column: x => x.IncidentId,
                        principalTable: "incidents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "police_cases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    IncidentId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileNumber = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Subject = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_police_cases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_police_cases_incidents_IncidentId",
                        column: x => x.IncidentId,
                        principalTable: "incidents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "incident_resources",
                columns: table => new
                {
                    IncidentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_incident_resources", x => new { x.IncidentId, x.ResourceId });
                    table.ForeignKey(
                        name: "FK_incident_resources_incidents_IncidentId",
                        column: x => x.IncidentId,
                        principalTable: "incidents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_incident_resources_operational_resources_ResourceId",
                        column: x => x.ResourceId,
                        principalTable: "operational_resources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "case_documents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PoliceCaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_case_documents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_case_documents_police_cases_PoliceCaseId",
                        column: x => x.PoliceCaseId,
                        principalTable: "police_cases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "case_persons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PoliceCaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    FirstName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DateOfBirth = table.Column<DateOnly>(type: "date", nullable: true),
                    Address = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Contact = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_case_persons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_case_persons_police_cases_PoliceCaseId",
                        column: x => x.PoliceCaseId,
                        principalTable: "police_cases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "evidence_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PoliceCaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    EvidenceNumber = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    StorageLocation = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SecuredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SecuredBy = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_evidence_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_evidence_items_police_cases_PoliceCaseId",
                        column: x => x.PoliceCaseId,
                        principalTable: "police_cases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "document_dispatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CaseDocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Recipient = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Reference = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Note = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    DispatchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DispatchedBy = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_dispatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_document_dispatches_case_documents_CaseDocumentId",
                        column: x => x.CaseDocumentId,
                        principalTable: "case_documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_address_register_Municipality_Street_HouseNumber",
                table: "address_register",
                columns: new[] { "Municipality", "Street", "HouseNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_address_register_Street",
                table: "address_register",
                column: "Street");

            migrationBuilder.CreateIndex(
                name: "IX_case_documents_PoliceCaseId",
                table: "case_documents",
                column: "PoliceCaseId");

            migrationBuilder.CreateIndex(
                name: "IX_case_persons_PoliceCaseId",
                table: "case_persons",
                column: "PoliceCaseId");

            migrationBuilder.CreateIndex(
                name: "IX_document_dispatches_CaseDocumentId",
                table: "document_dispatches",
                column: "CaseDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_evidence_items_PoliceCaseId_EvidenceNumber",
                table: "evidence_items",
                columns: new[] { "PoliceCaseId", "EvidenceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_incident_resources_ResourceId",
                table: "incident_resources",
                column: "ResourceId");

            migrationBuilder.CreateIndex(
                name: "IX_incident_status_history_IncidentId",
                table: "incident_status_history",
                column: "IncidentId");

            migrationBuilder.CreateIndex(
                name: "IX_incidents_OrganizationId_ReferenceNumber",
                table: "incidents",
                columns: new[] { "OrganizationId", "ReferenceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_operational_resources_OrganizationId_CallSign",
                table: "operational_resources",
                columns: new[] { "OrganizationId", "CallSign" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_police_cases_IncidentId",
                table: "police_cases",
                column: "IncidentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_police_cases_OrganizationId_FileNumber",
                table: "police_cases",
                columns: new[] { "OrganizationId", "FileNumber" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "address_register");

            migrationBuilder.DropTable(
                name: "case_persons");

            migrationBuilder.DropTable(
                name: "document_dispatches");

            migrationBuilder.DropTable(
                name: "evidence_items");

            migrationBuilder.DropTable(
                name: "incident_resources");

            migrationBuilder.DropTable(
                name: "incident_status_history");

            migrationBuilder.DropTable(
                name: "case_documents");

            migrationBuilder.DropTable(
                name: "operational_resources");

            migrationBuilder.DropTable(
                name: "police_cases");

            migrationBuilder.DropTable(
                name: "incidents");
        }
    }
}
