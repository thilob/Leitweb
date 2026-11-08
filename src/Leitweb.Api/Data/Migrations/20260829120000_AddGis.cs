using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Leitweb.Api.Data.Migrations;

[DbContext(typeof(LeitwebDbContext))]
[Migration("20260829120000_AddGis")]
public partial class AddGis : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS postgis;");
        migrationBuilder.CreateTable("gis_layers", table => new
        {
            Id = table.Column<Guid>(type: "uuid", nullable: false),
            OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
            Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
            Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
            Color = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
            IsEditable = table.Column<bool>(type: "boolean", nullable: false),
            CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
        }, constraints: table => table.PrimaryKey("PK_gis_layers", x => x.Id));
        migrationBuilder.CreateTable("gis_sources", table => new
        {
            Id = table.Column<Guid>(type: "uuid", nullable: false), OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
            Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false), ServiceType = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
            ServiceUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false), LayerName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
            Enabled = table.Column<bool>(type: "boolean", nullable: false), CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
        }, constraints: table => table.PrimaryKey("PK_gis_sources", x => x.Id));
        migrationBuilder.CreateTable("gis_map_profiles", table => new
        {
            Id = table.Column<Guid>(type: "uuid", nullable: false), OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
            UserSubject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false), Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
            ConfigurationJson = table.Column<string>(type: "jsonb", nullable: false), IsDefault = table.Column<bool>(type: "boolean", nullable: false), UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
        }, constraints: table => table.PrimaryKey("PK_gis_map_profiles", x => x.Id));
        migrationBuilder.CreateTable("gis_features", table => new
        {
            Id = table.Column<Guid>(type: "uuid", nullable: false), OrganizationId = table.Column<Guid>(type: "uuid", nullable: false), GisLayerId = table.Column<Guid>(type: "uuid", nullable: false),
            Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false), Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
            Geometry = table.Column<NetTopologySuite.Geometries.Geometry>(type: "geometry(Geometry,4326)", nullable: false), PropertiesJson = table.Column<string>(type: "jsonb", nullable: false),
            UpdatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false), UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
        }, constraints: table => { table.PrimaryKey("PK_gis_features", x => x.Id); table.ForeignKey("FK_gis_features_gis_layers_GisLayerId", x => x.GisLayerId, "gis_layers", "Id", onDelete: ReferentialAction.Cascade); });
        migrationBuilder.CreateIndex("IX_gis_layers_OrganizationId_Name", "gis_layers", new[] { "OrganizationId", "Name" }, unique: true);
        migrationBuilder.CreateIndex("IX_gis_sources_OrganizationId_Name", "gis_sources", new[] { "OrganizationId", "Name" }, unique: true);
        migrationBuilder.CreateIndex("IX_gis_map_profiles_OrganizationId_UserSubject_Name", "gis_map_profiles", new[] { "OrganizationId", "UserSubject", "Name" }, unique: true);
        migrationBuilder.CreateIndex("IX_gis_features_GisLayerId", "gis_features", "GisLayerId");
        migrationBuilder.CreateIndex("IX_gis_features_OrganizationId", "gis_features", "OrganizationId");
        migrationBuilder.CreateIndex("IX_gis_features_Geometry", "gis_features", "Geometry")
            .Annotation("Npgsql:IndexMethod", "gist");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("gis_features"); migrationBuilder.DropTable("gis_map_profiles"); migrationBuilder.DropTable("gis_sources"); migrationBuilder.DropTable("gis_layers");
    }
}
