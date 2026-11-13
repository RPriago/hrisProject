using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PeopleFlow.Persistence.Context;

#nullable disable

namespace PeopleFlow.Persistence.Migrations;

[DbContext(typeof(PeopleFlowDbContext))]
[Migration("20261113020000_TightenInfrastructureSecurityPolicies")]
public partial class TightenInfrastructureSecurityPolicies : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$
            BEGIN
                IF to_regclass('public.infra_outbox_messages') IS NOT NULL THEN
                    DROP POLICY IF EXISTS tenant_isolation_policy ON infra_outbox_messages;
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'peopleflow_app') THEN
                        CREATE POLICY tenant_isolation_policy ON infra_outbox_messages
                            AS PERMISSIVE
                            FOR ALL
                            TO peopleflow_app
                            USING ((tenant_id IS NOT NULL AND tenant_id = app_current_tenant_id()) OR app_is_outbox_dispatcher())
                            WITH CHECK ((tenant_id IS NOT NULL AND tenant_id = app_current_tenant_id()) OR app_is_outbox_dispatcher());
                    END IF;
                END IF;

                IF to_regclass('public.infra_data_protection_keys') IS NOT NULL AND EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'peopleflow_app') THEN
                    REVOKE UPDATE, DELETE ON TABLE infra_data_protection_keys FROM peopleflow_app;
                END IF;
            END $$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$
            BEGIN
                IF to_regclass('public.infra_outbox_messages') IS NOT NULL THEN
                    DROP POLICY IF EXISTS tenant_isolation_policy ON infra_outbox_messages;
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'peopleflow_app') THEN
                        CREATE POLICY tenant_isolation_policy ON infra_outbox_messages
                            AS PERMISSIVE
                            FOR ALL
                            TO peopleflow_app
                            USING (tenant_id IS NULL OR tenant_id = app_current_tenant_id() OR app_is_outbox_dispatcher())
                            WITH CHECK (tenant_id IS NULL OR tenant_id = app_current_tenant_id() OR app_is_outbox_dispatcher());
                    END IF;
                END IF;

                IF to_regclass('public.infra_data_protection_keys') IS NOT NULL AND EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'peopleflow_app') THEN
                    GRANT UPDATE, DELETE ON TABLE infra_data_protection_keys TO peopleflow_app;
                END IF;
            END $$;
            """);
    }
}
