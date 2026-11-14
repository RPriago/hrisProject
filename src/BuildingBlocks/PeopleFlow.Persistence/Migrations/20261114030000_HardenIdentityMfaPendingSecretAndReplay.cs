using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PeopleFlow.Persistence.Context;

#nullable disable

namespace PeopleFlow.Persistence.Migrations;

[DbContext(typeof(PeopleFlowDbContext))]
[Migration("20261114030000_HardenIdentityMfaPendingSecretAndReplay")]
public partial class HardenIdentityMfaPendingSecretAndReplay : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE identity_users
                ADD COLUMN IF NOT EXISTS pending_authenticator_key_encrypted varchar(512) NULL,
                ADD COLUMN IF NOT EXISTS pending_authenticator_key_expires_at_utc timestamptz NULL,
                ADD COLUMN IF NOT EXISTS last_totp_step_used bigint NULL;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE identity_users
                DROP COLUMN IF EXISTS last_totp_step_used,
                DROP COLUMN IF EXISTS pending_authenticator_key_expires_at_utc,
                DROP COLUMN IF EXISTS pending_authenticator_key_encrypted;
            """);
    }
}
