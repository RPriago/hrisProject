using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PeopleFlow.Persistence.Context;

#nullable disable

namespace PeopleFlow.Persistence.Migrations;

[DbContext(typeof(PeopleFlowDbContext))]
[Migration("20261114020000_AddLookupUserMembershipsFunction")]
public partial class AddLookupUserMembershipsFunction : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE OR REPLACE FUNCTION fn_lookup_active_memberships_by_user(p_user_id uuid)
            RETURNS TABLE(
                tenant_id uuid,
                tenant_slug varchar(64),
                tenant_name varchar(160),
                membership_status varchar(24),
                require_mfa_for_all boolean,
                enforce_payroll_sod boolean,
                tenant_status varchar(24),
                is_emergency_account boolean
            )
            LANGUAGE sql
            SECURITY DEFINER
            SET search_path = public, pg_temp
            AS $$
                SELECT
                    t.id AS tenant_id,
                    t.slug AS tenant_slug,
                    t.display_name AS tenant_name,
                    m.status AS membership_status,
                    t.require_mfa_for_all,
                    t.enforce_payroll_sod,
                    t.status AS tenant_status,
                    m.is_emergency_account
                FROM tenancy_memberships m
                INNER JOIN tenancy_tenants t ON t.id = m.tenant_id
                WHERE m.user_id = p_user_id
                  AND m.status = 'Active'
                  AND m.revoked_at_utc IS NULL
                  AND t.status <> 'Suspended'
                ORDER BY t.display_name;
            $$;

            DO $$
            BEGIN
                REVOKE ALL ON FUNCTION fn_lookup_active_memberships_by_user(uuid) FROM PUBLIC;
                IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'peopleflow_app') THEN
                    GRANT EXECUTE ON FUNCTION fn_lookup_active_memberships_by_user(uuid) TO peopleflow_app;
                END IF;
            END $$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP FUNCTION IF EXISTS fn_lookup_active_memberships_by_user(uuid);");
    }
}
