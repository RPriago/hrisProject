using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PeopleFlow.Persistence.Context;

namespace PeopleFlow.Persistence.Migrations;

[DbContext(typeof(PeopleFlowDbContext))]
[Migration("20261108000000_InitialCoreSchema")]
public partial class InitialCoreSchema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // 1. Table: infra_stored_files
        migrationBuilder.CreateTable(
            name: "infra_stored_files",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                owner_module = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                owner_entity_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                owner_entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                sensitivity_level = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                original_file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                content_type = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                size_bytes = table.Column<long>(type: "bigint", nullable: false),
                sha256_checksum = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: true),
                storage_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                bucket_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                scan_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                scanned_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                retention_category = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                uploaded_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: true),
                updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                updated_by = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_infra_stored_files", x => x.id);
                table.UniqueConstraint("AK_infra_stored_files_tenant_id_id", x => new { x.tenant_id, x.id });
            });

        // 2. Table: infra_background_job_runs
        migrationBuilder.CreateTable(
            name: "infra_background_job_runs",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                job_category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                reference_entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                hangfire_job_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                initiated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                total_items = table.Column<int>(type: "integer", nullable: true),
                succeeded_items = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                failed_items = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                error_report_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                error_details = table.Column<string>(type: "text", nullable: true),
                created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: true),
                updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                updated_by = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_infra_background_job_runs", x => x.id);
                table.UniqueConstraint("AK_infra_background_job_runs_tenant_id_id", x => new { x.tenant_id, x.id });
                table.ForeignKey(
                    name: "FK_infra_background_job_runs_infra_stored_files_tenant_id_err~",
                    columns: x => new { x.tenant_id, x.error_report_file_id },
                    principalTable: "infra_stored_files",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.SetNull);
            });

        // 3. Table: infra_idempotency_records
        migrationBuilder.CreateTable(
            name: "infra_idempotency_records",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                endpoint_key = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                idempotency_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                request_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                response_status_code = table.Column<int>(type: "integer", nullable: true),
                response_body_json = table.Column<string>(type: "jsonb", nullable: true),
                expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: true),
                updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                updated_by = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_infra_idempotency_records", x => x.id);
            });

        // 4. Table: infra_outbox_messages
        migrationBuilder.CreateTable(
            name: "infra_outbox_messages",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                message_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                payload_json = table.Column<string>(type: "jsonb", nullable: false),
                correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                attempt_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                next_attempt_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                last_error_summary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: true),
                processed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                updated_by = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_infra_outbox_messages", x => x.id);
            });

        // Indexes
        migrationBuilder.CreateIndex(
            name: "IX_infra_idempotency_records_expires_at_utc",
            table: "infra_idempotency_records",
            column: "expires_at_utc");

        migrationBuilder.CreateIndex(
            name: "IX_infra_idempotency_records_tenant_id_actor_user_id_endpoint~",
            table: "infra_idempotency_records",
            columns: new[] { "tenant_id", "actor_user_id", "endpoint_key", "idempotency_key" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_infra_outbox_messages_status_next_attempt_at_utc",
            table: "infra_outbox_messages",
            columns: new[] { "status", "next_attempt_at_utc" });

        migrationBuilder.CreateIndex(
            name: "IX_infra_background_job_runs_tenant_id_error_report_file_id",
            table: "infra_background_job_runs",
            columns: new[] { "tenant_id", "error_report_file_id" });

        // Row Level Security (RLS) DDL for tenant tables
        string[] rlsTables = ["infra_stored_files", "infra_background_job_runs", "infra_idempotency_records"];

        foreach (var table in rlsTables)
        {
            migrationBuilder.Sql($"ALTER TABLE {table} ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql($"ALTER TABLE {table} FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql($@"
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM pg_policies 
                        WHERE tablename = '{table}' AND policyname = 'tenant_isolation_policy'
                    ) THEN
                        CREATE POLICY tenant_isolation_policy ON {table}
                            AS PERMISSIVE
                            FOR ALL
                            TO peopleflow_app
                            USING (tenant_id = app_current_tenant_id())
                            WITH CHECK (tenant_id = app_current_tenant_id());
                    END IF;
                END $$;");
        }

        // Outbox messages allow tenant messages as well as platform messages (tenant_id IS NULL) or outbox dispatcher worker
        migrationBuilder.Sql(@"
            CREATE SCHEMA IF NOT EXISTS hangfire AUTHORIZATION peopleflow_migrator;
            GRANT USAGE, CREATE ON SCHEMA hangfire TO peopleflow_app;
            GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA hangfire TO peopleflow_app;
            GRANT ALL PRIVILEGES ON ALL SEQUENCES IN SCHEMA hangfire TO peopleflow_app;
            ALTER DEFAULT PRIVILEGES FOR ROLE peopleflow_migrator IN SCHEMA hangfire GRANT ALL PRIVILEGES ON TABLES TO peopleflow_app;
            ALTER DEFAULT PRIVILEGES FOR ROLE peopleflow_migrator IN SCHEMA hangfire GRANT ALL PRIVILEGES ON SEQUENCES TO peopleflow_app;

            CREATE OR REPLACE FUNCTION app_is_outbox_dispatcher() RETURNS boolean
            LANGUAGE sql STABLE AS $$
                SELECT COALESCE(current_setting('app.outbox_dispatcher_mode', true), '') = 'true';
            $$;
            GRANT EXECUTE ON FUNCTION app_is_outbox_dispatcher() TO peopleflow_app;
            GRANT EXECUTE ON FUNCTION app_is_outbox_dispatcher() TO peopleflow_migrator;
        ");

        migrationBuilder.Sql("ALTER TABLE infra_outbox_messages ENABLE ROW LEVEL SECURITY;");
        migrationBuilder.Sql("ALTER TABLE infra_outbox_messages FORCE ROW LEVEL SECURITY;");
        migrationBuilder.Sql(@"
            DO $$
            BEGIN
                IF EXISTS (
                    SELECT 1 FROM pg_policies 
                    WHERE tablename = 'infra_outbox_messages' AND policyname = 'tenant_isolation_policy'
                ) THEN
                    DROP POLICY tenant_isolation_policy ON infra_outbox_messages;
                END IF;

                CREATE POLICY tenant_isolation_policy ON infra_outbox_messages
                    AS PERMISSIVE
                    FOR ALL
                    TO peopleflow_app
                    USING (tenant_id IS NULL OR tenant_id = app_current_tenant_id() OR app_is_outbox_dispatcher())
                    WITH CHECK (tenant_id IS NULL OR tenant_id = app_current_tenant_id() OR app_is_outbox_dispatcher());
            END $$;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "infra_background_job_runs");
        migrationBuilder.DropTable(name: "infra_stored_files");
        migrationBuilder.DropTable(name: "infra_idempotency_records");
        migrationBuilder.DropTable(name: "infra_outbox_messages");
    }
}
