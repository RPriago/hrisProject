using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PeopleFlow.Persistence.Context;

#nullable disable

namespace PeopleFlow.Persistence.Migrations;

[DbContext(typeof(PeopleFlowDbContext))]
[Migration("20261114010000_AddModul02IdentityTenancySubscriptionSchema")]
public partial class AddModul02IdentityTenancySubscriptionSchema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            -- =========================================================================
            -- 1. GLOBAL / PLATFORM TABLES (No tenant RLS, access controlled by Modul 02)
            -- =========================================================================

            CREATE TABLE IF NOT EXISTS infra_platform_audit_logs (
                id uuid PRIMARY KEY,
                actor_user_id uuid NULL,
                actor_role varchar(64) NOT NULL,
                action varchar(120) NOT NULL,
                target_tenant_id uuid NULL,
                support_grant_id uuid NULL,
                resource_path varchar(256) NOT NULL,
                correlation_id uuid NOT NULL,
                reason varchar(500) NULL,
                metadata_json jsonb NULL,
                occurred_at_utc timestamptz NOT NULL,
                created_at_utc timestamptz NOT NULL,
                created_by uuid NULL,
                updated_at_utc timestamptz NULL,
                updated_by uuid NULL
            );
            CREATE INDEX IF NOT EXISTS "IX_infra_platform_audit_logs_target_tenant_id_occurred_at_utc"
                ON infra_platform_audit_logs (target_tenant_id, occurred_at_utc);

            CREATE TABLE IF NOT EXISTS identity_users (
                id uuid PRIMARY KEY,
                email varchar(256) NOT NULL,
                normalized_email varchar(256) NOT NULL,
                password_hash varchar(512) NOT NULL,
                two_factor_enabled boolean NOT NULL DEFAULT false,
                authenticator_key_encrypted varchar(512) NULL,
                recovery_codes_hash_json jsonb NULL,
                access_failed_count integer NOT NULL DEFAULT 0,
                lockout_end_utc timestamptz NULL,
                security_stamp varchar(64) NOT NULL,
                preferred_language varchar(10) NOT NULL DEFAULT 'id-ID',
                is_platform_admin boolean NOT NULL DEFAULT false,
                is_break_glass boolean NOT NULL DEFAULT false,
                is_active boolean NOT NULL DEFAULT true,
                created_at_utc timestamptz NOT NULL,
                created_by uuid NULL,
                updated_at_utc timestamptz NULL,
                updated_by uuid NULL,
                CONSTRAINT uq_identity_users_normalized_email UNIQUE (normalized_email)
            );

            CREATE TABLE IF NOT EXISTS identity_user_sessions (
                id uuid PRIMARY KEY,
                user_id uuid NOT NULL REFERENCES identity_users(id) ON DELETE CASCADE,
                active_tenant_id uuid NULL,
                support_grant_id uuid NULL,
                mfa_verified_at_utc timestamptz NULL,
                ip_address_masked varchar(64) NULL,
                user_agent_summary varchar(255) NULL,
                created_at_utc timestamptz NOT NULL,
                created_by uuid NULL,
                updated_at_utc timestamptz NULL,
                updated_by uuid NULL,
                last_seen_at_utc timestamptz NOT NULL,
                expires_at_utc timestamptz NOT NULL,
                revoked_at_utc timestamptz NULL,
                revoke_reason varchar(64) NULL
            );
            CREATE INDEX IF NOT EXISTS ix_identity_user_sessions_user_revoked
                ON identity_user_sessions (user_id, revoked_at_utc);
            CREATE INDEX IF NOT EXISTS ix_identity_user_sessions_active_tenant_user
                ON identity_user_sessions (active_tenant_id, user_id);

            CREATE TABLE IF NOT EXISTS identity_password_reset_tokens (
                id uuid PRIMARY KEY,
                user_id uuid NOT NULL REFERENCES identity_users(id) ON DELETE CASCADE,
                token_hash char(64) NOT NULL,
                expires_at_utc timestamptz NOT NULL,
                used_at_utc timestamptz NULL,
                created_at_utc timestamptz NOT NULL,
                created_by uuid NULL,
                updated_at_utc timestamptz NULL,
                updated_by uuid NULL,
                CONSTRAINT uq_identity_password_reset_tokens_hash UNIQUE (token_hash)
            );

            CREATE TABLE IF NOT EXISTS tenancy_tenants (
                id uuid PRIMARY KEY,
                slug varchar(64) NOT NULL,
                display_name varchar(200) NOT NULL,
                industry_preset_code varchar(48) NOT NULL,
                default_language varchar(10) NOT NULL DEFAULT 'id-ID',
                default_time_zone_id varchar(64) NOT NULL DEFAULT 'Asia/Jakarta',
                require_mfa_for_all boolean NOT NULL DEFAULT false,
                enforce_payroll_sod boolean NOT NULL DEFAULT true,
                status varchar(24) NOT NULL DEFAULT 'Active',
                created_at_utc timestamptz NOT NULL,
                created_by uuid NULL,
                updated_at_utc timestamptz NULL,
                updated_by uuid NULL,
                CONSTRAINT uq_tenancy_tenants_slug UNIQUE (slug)
            );

            CREATE TABLE IF NOT EXISTS tenancy_support_access_grants (
                id uuid PRIMARY KEY,
                tenant_id uuid NOT NULL REFERENCES tenancy_tenants(id) ON DELETE CASCADE,
                ticket_reference varchar(80) NOT NULL,
                reason varchar(500) NOT NULL,
                access_level varchar(40) NOT NULL,
                allow_financial_read boolean NOT NULL DEFAULT false,
                approved_by_user_id uuid NOT NULL REFERENCES identity_users(id),
                assigned_admin_user_id uuid NULL REFERENCES identity_users(id),
                starts_at_utc timestamptz NOT NULL,
                expires_at_utc timestamptz NOT NULL,
                revoked_at_utc timestamptz NULL,
                created_at_utc timestamptz NOT NULL,
                created_by uuid NULL,
                updated_at_utc timestamptz NULL,
                updated_by uuid NULL
            );

            CREATE TABLE IF NOT EXISTS subscription_plans (
                id uuid PRIMARY KEY,
                code varchar(48) NOT NULL,
                name varchar(120) NOT NULL,
                enabled_modules_json jsonb NOT NULL,
                max_active_employees integer NOT NULL,
                max_storage_bytes bigint NOT NULL,
                max_api_clients integer NOT NULL,
                monthly_price_amount numeric(19,2) NOT NULL,
                currency varchar(3) NOT NULL DEFAULT 'IDR',
                is_active boolean NOT NULL DEFAULT true,
                created_at_utc timestamptz NOT NULL,
                created_by uuid NULL,
                updated_at_utc timestamptz NULL,
                updated_by uuid NULL,
                CONSTRAINT uq_subscription_plans_code UNIQUE (code)
            );

            CREATE TABLE IF NOT EXISTS subscription_tenant_subscriptions (
                id uuid PRIMARY KEY,
                tenant_id uuid NOT NULL REFERENCES tenancy_tenants(id) ON DELETE CASCADE,
                plan_id uuid NOT NULL REFERENCES subscription_plans(id),
                status varchar(24) NOT NULL,
                current_period_start_utc timestamptz NOT NULL,
                current_period_end_utc timestamptz NOT NULL,
                trial_ends_at_utc timestamptz NULL,
                grace_period_ends_at_utc timestamptz NULL,
                read_only_since_utc timestamptz NULL,
                override_max_employees integer NULL,
                override_max_storage_bytes bigint NULL,
                created_at_utc timestamptz NOT NULL,
                created_by uuid NULL,
                updated_at_utc timestamptz NULL,
                updated_by uuid NULL,
                CONSTRAINT uq_subscription_tenant_subscriptions_tenant UNIQUE (tenant_id)
            );

            CREATE TABLE IF NOT EXISTS subscription_invoices (
                id uuid PRIMARY KEY,
                tenant_id uuid NOT NULL REFERENCES tenancy_tenants(id) ON DELETE CASCADE,
                invoice_number varchar(64) NOT NULL,
                amount numeric(19,2) NOT NULL,
                currency varchar(3) NOT NULL,
                status varchar(24) NOT NULL,
                payment_channel varchar(32) NOT NULL,
                payment_reference varchar(160) NULL,
                paid_at_utc timestamptz NULL,
                recorded_by_admin_id uuid NULL REFERENCES identity_users(id),
                notes varchar(500) NULL,
                created_at_utc timestamptz NOT NULL,
                created_by uuid NULL,
                updated_at_utc timestamptz NULL,
                updated_by uuid NULL,
                CONSTRAINT uq_subscription_invoices_number UNIQUE (invoice_number)
            );

            CREATE TABLE IF NOT EXISTS subscription_billing_webhook_events (
                id uuid PRIMARY KEY,
                provider_name varchar(48) NOT NULL,
                provider_event_id varchar(160) NOT NULL,
                event_type varchar(100) NOT NULL,
                signature_verified boolean NOT NULL,
                payload_json jsonb NOT NULL,
                processing_status varchar(32) NOT NULL,
                attempt_count integer NOT NULL DEFAULT 0,
                error_summary varchar(1000) NULL,
                received_at_utc timestamptz NOT NULL,
                processed_at_utc timestamptz NULL,
                created_at_utc timestamptz NOT NULL,
                created_by uuid NULL,
                updated_at_utc timestamptz NULL,
                updated_by uuid NULL,
                CONSTRAINT uq_billing_webhook_provider_event UNIQUE (provider_name, provider_event_id)
            );

            -- =========================================================================
            -- 2. TENANT-OWNED TABLES (Mandatory tenant_id + Forced PostgreSQL RLS)
            -- =========================================================================

            CREATE TABLE IF NOT EXISTS tenancy_memberships (
                id uuid PRIMARY KEY,
                tenant_id uuid NOT NULL REFERENCES tenancy_tenants(id) ON DELETE CASCADE,
                user_id uuid NOT NULL REFERENCES identity_users(id) ON DELETE CASCADE,
                status varchar(24) NOT NULL DEFAULT 'Active',
                is_emergency_account boolean NOT NULL DEFAULT false,
                joined_at_utc timestamptz NOT NULL,
                revoked_at_utc timestamptz NULL,
                revoke_reason varchar(120) NULL,
                created_at_utc timestamptz NOT NULL,
                created_by uuid NULL,
                updated_at_utc timestamptz NULL,
                updated_by uuid NULL,
                CONSTRAINT uq_tenancy_memberships_tenant_id UNIQUE (tenant_id, id),
                CONSTRAINT uq_tenancy_memberships_tenant_user UNIQUE (tenant_id, user_id)
            );

            CREATE TABLE IF NOT EXISTS tenancy_invitations (
                id uuid PRIMARY KEY,
                tenant_id uuid NOT NULL REFERENCES tenancy_tenants(id) ON DELETE CASCADE,
                invited_email varchar(256) NOT NULL,
                target_employee_id uuid NULL,
                initial_role_assignments_json jsonb NOT NULL,
                token_hash char(64) NOT NULL,
                status varchar(24) NOT NULL DEFAULT 'Pending',
                expires_at_utc timestamptz NOT NULL,
                invited_by_user_id uuid NOT NULL REFERENCES identity_users(id),
                accepted_by_user_id uuid NULL REFERENCES identity_users(id),
                created_at_utc timestamptz NOT NULL,
                created_by uuid NULL,
                updated_at_utc timestamptz NULL,
                updated_by uuid NULL,
                CONSTRAINT uq_tenancy_invitations_token_hash UNIQUE (token_hash)
            );

            CREATE TABLE IF NOT EXISTS tenancy_roles (
                id uuid PRIMARY KEY,
                tenant_id uuid NOT NULL REFERENCES tenancy_tenants(id) ON DELETE CASCADE,
                code varchar(64) NOT NULL,
                name varchar(120) NOT NULL,
                description varchar(400) NULL,
                is_system_builtin boolean NOT NULL DEFAULT false,
                is_read_only_role boolean NOT NULL DEFAULT false,
                created_at_utc timestamptz NOT NULL,
                created_by uuid NULL,
                updated_at_utc timestamptz NULL,
                updated_by uuid NULL,
                CONSTRAINT uq_tenancy_roles_tenant_id UNIQUE (tenant_id, id),
                CONSTRAINT uq_tenancy_roles_tenant_code UNIQUE (tenant_id, code)
            );

            CREATE TABLE IF NOT EXISTS tenancy_role_permissions (
                tenant_id uuid NOT NULL,
                role_id uuid NOT NULL,
                permission_code varchar(100) NOT NULL,
                CONSTRAINT pk_tenancy_role_permissions PRIMARY KEY (tenant_id, role_id, permission_code),
                CONSTRAINT fk_tenancy_role_permissions_role FOREIGN KEY (tenant_id, role_id)
                    REFERENCES tenancy_roles(tenant_id, id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS tenancy_member_role_scopes (
                id uuid PRIMARY KEY,
                tenant_id uuid NOT NULL,
                membership_id uuid NOT NULL,
                role_id uuid NOT NULL,
                scope_type varchar(24) NOT NULL,
                scope_target_ids_json jsonb NULL,
                data_period_from date NULL,
                data_period_to date NULL,
                valid_until_utc timestamptz NULL,
                created_at_utc timestamptz NOT NULL,
                created_by uuid NULL,
                updated_at_utc timestamptz NULL,
                updated_by uuid NULL,
                CONSTRAINT fk_tenancy_mrs_membership FOREIGN KEY (tenant_id, membership_id)
                    REFERENCES tenancy_memberships(tenant_id, id) ON DELETE CASCADE,
                CONSTRAINT fk_tenancy_mrs_role FOREIGN KEY (tenant_id, role_id)
                    REFERENCES tenancy_roles(tenant_id, id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS tenancy_api_integration_clients (
                id uuid PRIMARY KEY,
                tenant_id uuid NOT NULL REFERENCES tenancy_tenants(id) ON DELETE CASCADE,
                name varchar(120) NOT NULL,
                client_id varchar(80) NOT NULL,
                active_secret_hash varchar(255) NOT NULL,
                previous_secret_hash varchar(255) NULL,
                previous_secret_expires_at_utc timestamptz NULL,
                allowed_scopes_json jsonb NOT NULL,
                scoped_legal_entity_ids_json jsonb NULL,
                scoped_location_ids_json jsonb NULL,
                rate_limit_per_minute integer NOT NULL DEFAULT 60,
                expires_at_utc timestamptz NOT NULL,
                revoked_at_utc timestamptz NULL,
                last_used_at_utc timestamptz NULL,
                created_at_utc timestamptz NOT NULL,
                created_by uuid NULL,
                updated_at_utc timestamptz NULL,
                updated_by uuid NULL,
                CONSTRAINT uq_tenancy_api_clients_client_id UNIQUE (client_id)
            );

            -- =========================================================================
            -- 3. RLS POLICIES & SECURITY DEFINER FUNCTIONS
            -- =========================================================================

            DO $$
            DECLARE
                tbl text;
                rls_tables text[] := ARRAY[
                    'tenancy_memberships',
                    'tenancy_invitations',
                    'tenancy_roles',
                    'tenancy_role_permissions',
                    'tenancy_member_role_scopes',
                    'tenancy_api_integration_clients'
                ];
            BEGIN
                FOREACH tbl IN ARRAY rls_tables LOOP
                    EXECUTE format('ALTER TABLE %I ENABLE ROW LEVEL SECURITY;', tbl);
                    EXECUTE format('ALTER TABLE %I FORCE ROW LEVEL SECURITY;', tbl);
                    EXECUTE format('DROP POLICY IF EXISTS tenant_isolation_policy ON %I;', tbl);
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'peopleflow_app') THEN
                        EXECUTE format(
                            'CREATE POLICY tenant_isolation_policy ON %I AS PERMISSIVE FOR ALL TO peopleflow_app USING (tenant_id = app_current_tenant_id()) WITH CHECK (tenant_id = app_current_tenant_id());',
                            tbl);
                        EXECUTE format('GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE %I TO peopleflow_app;', tbl);
                    END IF;
                END LOOP;

                IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'peopleflow_app') THEN
                    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE
                        identity_users, identity_user_sessions, identity_password_reset_tokens,
                        tenancy_tenants, tenancy_support_access_grants,
                        subscription_plans, subscription_tenant_subscriptions,
                        subscription_invoices, subscription_billing_webhook_events
                        TO peopleflow_app;

                    GRANT SELECT, INSERT ON TABLE infra_platform_audit_logs TO peopleflow_app;
                    REVOKE UPDATE, DELETE ON TABLE infra_platform_audit_logs FROM peopleflow_app;
                END IF;
            END $$;

            CREATE OR REPLACE FUNCTION fn_lookup_invitation_by_token_hash(p_token_hash char(64))
            RETURNS TABLE(id uuid, tenant_id uuid, invited_email varchar(256), status varchar(24), expires_at_utc timestamptz)
            LANGUAGE sql
            SECURITY DEFINER
            SET search_path = public, pg_temp
            AS $$
                SELECT i.id, i.tenant_id, i.invited_email, i.status, i.expires_at_utc
                FROM tenancy_invitations i
                WHERE i.token_hash = p_token_hash
                LIMIT 1;
            $$;

            DO $$
            BEGIN
                REVOKE ALL ON FUNCTION fn_lookup_invitation_by_token_hash(char(64)) FROM PUBLIC;
                IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'peopleflow_app') THEN
                    GRANT EXECUTE ON FUNCTION fn_lookup_invitation_by_token_hash(char(64)) TO peopleflow_app;
                END IF;
            END $$;

            -- =========================================================================
            -- 4. DEFAULT SAAS SUBSCRIPTION PLANS SEED
            -- =========================================================================

            INSERT INTO subscription_plans
                (id, code, name, enabled_modules_json, max_active_employees, max_storage_bytes, max_api_clients, monthly_price_amount, currency, is_active, created_at_utc)
            VALUES
                ('01930000-0000-7000-8000-000000000001', 'STARTER', 'Starter HR & Payroll', '["CoreHR","Time","Payroll"]'::jsonb, 50, 5368709120, 2, 1500000.00, 'IDR', true, now()),
                ('01930000-0000-7000-8000-000000000002', 'GROWTH', 'Growth Workforce Suite', '["CoreHR","Time","Payroll","Leave","Expense","Performance"]'::jsonb, 250, 53687091200, 10, 4500000.00, 'IDR', true, now()),
                ('01930000-0000-7000-8000-000000000003', 'ENTERPRISE_DEMO', 'Enterprise Full Suite', '["CoreHR","Time","Payroll","Leave","Expense","Performance","Recruitment","Learning","Analytics"]'::jsonb, 5000, 536870912000, 50, 15000000.00, 'IDR', true, now())
            ON CONFLICT (code) DO NOTHING;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP FUNCTION IF EXISTS fn_lookup_invitation_by_token_hash(char(64));
            DROP TABLE IF EXISTS tenancy_api_integration_clients;
            DROP TABLE IF EXISTS tenancy_member_role_scopes;
            DROP TABLE IF EXISTS tenancy_role_permissions;
            DROP TABLE IF EXISTS tenancy_roles;
            DROP TABLE IF EXISTS tenancy_invitations;
            DROP TABLE IF EXISTS tenancy_memberships;
            DROP TABLE IF EXISTS subscription_billing_webhook_events;
            DROP TABLE IF EXISTS subscription_invoices;
            DROP TABLE IF EXISTS subscription_tenant_subscriptions;
            DROP TABLE IF EXISTS subscription_plans;
            DROP TABLE IF EXISTS tenancy_support_access_grants;
            DROP TABLE IF EXISTS tenancy_tenants;
            DROP TABLE IF EXISTS identity_password_reset_tokens;
            DROP TABLE IF EXISTS identity_user_sessions;
            DROP TABLE IF EXISTS identity_users;
            DROP TABLE IF EXISTS infra_platform_audit_logs;
            """);
    }
}
