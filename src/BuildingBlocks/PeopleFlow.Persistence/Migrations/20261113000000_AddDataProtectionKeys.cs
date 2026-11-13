using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PeopleFlow.Persistence.Context;

namespace PeopleFlow.Persistence.Migrations;

[DbContext(typeof(PeopleFlowDbContext))]
[Migration("20261113000000_AddDataProtectionKeys")]
public partial class AddDataProtectionKeys : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "infra_data_protection_keys",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                friendly_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                xml = table.Column<string>(type: "text", nullable: false),
                created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_infra_data_protection_keys", x => x.id);
            });

        migrationBuilder.Sql(@"
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'peopleflow_app') THEN
                    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE infra_data_protection_keys TO peopleflow_app;
                END IF;
            END $$;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "infra_data_protection_keys");
    }
}
