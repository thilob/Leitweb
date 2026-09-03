using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Leitweb.Api.Data.Migrations;

[DbContext(typeof(LeitwebDbContext))]
[Migration("20260904090000_AddAddressSearchIndex")]
public partial class AddAddressSearchIndex : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            "CREATE INDEX \"IX_address_register_Search\" " +
            "ON address_register (lower(\"Street\") text_pattern_ops, lower(\"HouseNumber\") text_pattern_ops);");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_address_register_Search\";");
    }
}
