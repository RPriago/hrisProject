using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PeopleFlow.Persistence.Context;

#nullable disable

namespace PeopleFlow.Persistence.Migrations;

[DbContext(typeof(PeopleFlowDbContext))]
[Migration("20261114040000_AddEnqueuePlatformOutboxMessageFunction")]
public partial class AddEnqueuePlatformOutboxMessageFunction : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE OR REPLACE FUNCTION fn_enqueue_platform_outbox_message(
                p_id uuid,
                p_message_type varchar(200),
                p_payload_json jsonb,
                p_correlation_id uuid,
                p_created_at_utc timestamptz,
                p_next_attempt_at_utc timestamptz,
                p_created_by uuid DEFAULT NULL
            )
            RETURNS void
            LANGUAGE plpgsql
            SECURITY DEFINER
            SET search_path = public, pg_temp
            AS $$
            DECLARE
                v_prev_dispatcher_mode text;
            BEGIN
                IF p_message_type IS NULL OR p_message_type NOT IN (
                    'Security.BreakGlassLoginAlert',
                    'Identity.PasswordResetRequested'
                ) THEN
                    RAISE EXCEPTION 'Unauthorized platform outbox message type: %', p_message_type
                        USING ERRCODE = '42501';
                END IF;

                v_prev_dispatcher_mode := current_setting('app.outbox_dispatcher_mode', true);
                PERFORM set_config('app.outbox_dispatcher_mode', 'true', true);

                INSERT INTO infra_outbox_messages (
                    id,
                    tenant_id,
                    message_type,
                    payload_json,
                    correlation_id,
                    status,
                    attempt_count,
                    next_attempt_at_utc,
                    created_at_utc,
                    created_by
                )
                VALUES (
                    p_id,
                    NULL,
                    p_message_type,
                    p_payload_json,
                    p_correlation_id,
                    'Pending',
                    0,
                    p_next_attempt_at_utc,
                    p_created_at_utc,
                    p_created_by
                );

                PERFORM set_config('app.outbox_dispatcher_mode', COALESCE(v_prev_dispatcher_mode, 'false'), true);
            EXCEPTION
                WHEN OTHERS THEN
                    IF v_prev_dispatcher_mode IS NOT NULL THEN
                        PERFORM set_config('app.outbox_dispatcher_mode', v_prev_dispatcher_mode, true);
                    ELSE
                        PERFORM set_config('app.outbox_dispatcher_mode', 'false', true);
                    END IF;
                    RAISE;
            END;
            $$;

            DO $$
            BEGIN
                REVOKE ALL ON FUNCTION fn_enqueue_platform_outbox_message(uuid, varchar, jsonb, uuid, timestamptz, timestamptz, uuid) FROM PUBLIC;
                IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'peopleflow_app') THEN
                    GRANT EXECUTE ON FUNCTION fn_enqueue_platform_outbox_message(uuid, varchar, jsonb, uuid, timestamptz, timestamptz, uuid) TO peopleflow_app;
                END IF;
            END $$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP FUNCTION IF EXISTS fn_enqueue_platform_outbox_message(uuid, varchar, jsonb, uuid, timestamptz, timestamptz, uuid);");
    }
}
