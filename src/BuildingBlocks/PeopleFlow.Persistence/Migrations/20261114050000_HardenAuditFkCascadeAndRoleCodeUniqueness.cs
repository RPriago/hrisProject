using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PeopleFlow.Persistence.Context;

#nullable disable

namespace PeopleFlow.Persistence.Migrations;

[DbContext(typeof(PeopleFlowDbContext))]
[Migration("20261114050000_HardenAuditFkCascadeAndRoleCodeUniqueness")]
public partial class HardenAuditFkCascadeAndRoleCodeUniqueness : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$
            DECLARE
                v_rec record;
            BEGIN
                FOR v_rec IN
                    SELECT conname
                    FROM pg_constraint
                    WHERE contype = 'f'
                      AND conrelid = 'tenancy_support_access_grants'::regclass
                      AND confrelid = 'tenancy_tenants'::regclass
                LOOP
                    EXECUTE format('ALTER TABLE tenancy_support_access_grants DROP CONSTRAINT %I', v_rec.conname);
                END LOOP;

                ALTER TABLE tenancy_support_access_grants
                    ADD CONSTRAINT fk_tenancy_support_access_grants_tenant
                    FOREIGN KEY (tenant_id) REFERENCES tenancy_tenants(id) ON DELETE RESTRICT;

                FOR v_rec IN
                    SELECT conname
                    FROM pg_constraint
                    WHERE contype = 'f'
                      AND conrelid = 'subscription_invoices'::regclass
                      AND confrelid = 'tenancy_tenants'::regclass
                LOOP
                    EXECUTE format('ALTER TABLE subscription_invoices DROP CONSTRAINT %I', v_rec.conname);
                END LOOP;

                ALTER TABLE subscription_invoices
                    ADD CONSTRAINT fk_subscription_invoices_tenant
                    FOREIGN KEY (tenant_id) REFERENCES tenancy_tenants(id) ON DELETE RESTRICT;
            END $$;

            CREATE UNIQUE INDEX IF NOT EXISTS uq_tenancy_roles_tenant_code_ci
                ON tenancy_roles (tenant_id, UPPER(code));
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP INDEX IF EXISTS uq_tenancy_roles_tenant_code_ci;

            ALTER TABLE tenancy_support_access_grants
                DROP CONSTRAINT IF EXISTS fk_tenancy_support_access_grants_tenant;
            ALTER TABLE tenancy_support_access_grants
                ADD CONSTRAINT fk_tenancy_support_access_grants_tenant
                FOREIGN KEY (tenant_id) REFERENCES tenancy_tenants(id) ON DELETE CASCADE;

            ALTER TABLE subscription_invoices
                DROP CONSTRAINT IF EXISTS fk_subscription_invoices_tenant;
            ALTER TABLE subscription_invoices
                ADD CONSTRAINT fk_subscription_invoices_tenant
                FOREIGN KEY (tenant_id) REFERENCES tenancy_tenants(id) ON DELETE CASCADE;
            """);
    }
}
