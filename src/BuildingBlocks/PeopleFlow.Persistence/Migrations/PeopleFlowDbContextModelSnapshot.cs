using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using PeopleFlow.Persistence.Context;

#nullable disable

namespace PeopleFlow.Persistence.Migrations;

[DbContext(typeof(PeopleFlowDbContext))]
public partial class PeopleFlowDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
#pragma warning disable 612, 618
        modelBuilder
            .HasAnnotation("ProductVersion", "9.0.0")
            .HasAnnotation("Relational:MaxIdentifierLength", 63);

        NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

        modelBuilder.Entity("PeopleFlow.Persistence.Entities.BackgroundJobRun", b =>
        {
            b.Property<Guid>("Id")
                .ValueGeneratedOnAdd()
                .HasColumnType("uuid")
                .HasColumnName("id");

            b.Property<Guid>("TenantId")
                .HasColumnType("uuid")
                .HasColumnName("tenant_id");

            b.Property<DateTimeOffset?>("CompletedAtUtc")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("completed_at_utc");

            b.Property<Guid>("CorrelationId")
                .HasColumnType("uuid")
                .HasColumnName("correlation_id");

            b.Property<DateTimeOffset>("CreatedAtUtc")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("created_at_utc");

            b.Property<Guid?>("CreatedBy")
                .HasColumnType("uuid")
                .HasColumnName("created_by");

            b.Property<string>("ErrorDetails")
                .HasColumnType("text")
                .HasColumnName("error_details");

            b.Property<Guid?>("ErrorReportFileId")
                .HasColumnType("uuid")
                .HasColumnName("error_report_file_id");

            b.Property<int>("FailedItems")
                .ValueGeneratedOnAdd()
                .HasColumnType("integer")
                .HasDefaultValue(0)
                .HasColumnName("failed_items");

            b.Property<string>("HangfireJobId")
                .HasMaxLength(64)
                .HasColumnType("character varying(64)")
                .HasColumnName("hangfire_job_id");

            b.Property<Guid?>("InitiatedByUserId")
                .HasColumnType("uuid")
                .HasColumnName("initiated_by_user_id");

            b.Property<string>("JobCategory")
                .IsRequired()
                .HasMaxLength(64)
                .HasColumnType("character varying(64)")
                .HasColumnName("job_category");

            b.Property<Guid?>("ReferenceEntityId")
                .HasColumnType("uuid")
                .HasColumnName("reference_entity_id");

            b.Property<DateTimeOffset?>("StartedAtUtc")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("started_at_utc");

            b.Property<string>("Status")
                .IsRequired()
                .HasMaxLength(32)
                .HasColumnType("character varying(32)")
                .HasColumnName("status");

            b.Property<int>("SucceededItems")
                .ValueGeneratedOnAdd()
                .HasColumnType("integer")
                .HasDefaultValue(0)
                .HasColumnName("succeeded_items");

            b.Property<int?>("TotalItems")
                .HasColumnType("integer")
                .HasColumnName("total_items");

            b.Property<DateTimeOffset?>("UpdatedAtUtc")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("updated_at_utc");

            b.Property<Guid?>("UpdatedBy")
                .HasColumnType("uuid")
                .HasColumnName("updated_by");

            b.Property<uint>("xmin")
                .IsConcurrencyToken()
                .ValueGeneratedOnAddOrUpdate()
                .HasColumnType("xid")
                .HasColumnName("xmin")
                .HasDefaultValue(0u);

            b.HasKey("Id");

            b.HasAlternateKey("TenantId", "Id");

            b.HasIndex("TenantId", "ErrorReportFileId");

            b.ToTable("infra_background_job_runs", (string)null);
        });

        modelBuilder.Entity("PeopleFlow.Persistence.Entities.IdempotencyRecord", b =>
        {
            b.Property<Guid>("Id")
                .ValueGeneratedOnAdd()
                .HasColumnType("uuid")
                .HasColumnName("id");

            b.Property<Guid>("TenantId")
                .HasColumnType("uuid")
                .HasColumnName("tenant_id");

            b.Property<Guid>("ActorUserId")
                .HasColumnType("uuid")
                .HasColumnName("actor_user_id");

            b.Property<DateTimeOffset>("CreatedAtUtc")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("created_at_utc");

            b.Property<Guid?>("CreatedBy")
                .HasColumnType("uuid")
                .HasColumnName("created_by");

            b.Property<string>("EndpointKey")
                .IsRequired()
                .HasMaxLength(160)
                .HasColumnType("character varying(160)")
                .HasColumnName("endpoint_key");

            b.Property<DateTimeOffset>("ExpiresAtUtc")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("expires_at_utc");

            b.Property<string>("IdempotencyKey")
                .IsRequired()
                .HasMaxLength(128)
                .HasColumnType("character varying(128)")
                .HasColumnName("idempotency_key");

            b.Property<string>("RequestHash")
                .IsRequired()
                .HasMaxLength(64)
                .HasColumnType("character(64)")
                .IsFixedLength()
                .HasColumnName("request_hash");

            b.Property<string>("ResponseBodyJson")
                .HasColumnType("jsonb")
                .HasColumnName("response_body_json");

            b.Property<int?>("ResponseStatusCode")
                .HasColumnType("integer")
                .HasColumnName("response_status_code");

            b.Property<string>("Status")
                .IsRequired()
                .HasMaxLength(24)
                .HasColumnType("character varying(24)")
                .HasColumnName("status");

            b.Property<DateTimeOffset?>("UpdatedAtUtc")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("updated_at_utc");

            b.Property<Guid?>("UpdatedBy")
                .HasColumnType("uuid")
                .HasColumnName("updated_by");

            b.Property<uint>("xmin")
                .IsConcurrencyToken()
                .ValueGeneratedOnAddOrUpdate()
                .HasColumnType("xid")
                .HasColumnName("xmin")
                .HasDefaultValue(0u);

            b.HasKey("Id");

            b.HasIndex("ExpiresAtUtc");

            b.HasIndex("TenantId", "ActorUserId", "EndpointKey", "IdempotencyKey")
                .IsUnique();

            b.ToTable("infra_idempotency_records", (string)null);
        });

        modelBuilder.Entity("PeopleFlow.Persistence.Entities.OutboxMessage", b =>
        {
            b.Property<Guid>("Id")
                .ValueGeneratedOnAdd()
                .HasColumnType("uuid")
                .HasColumnName("id");

            b.Property<Guid?>("TenantId")
                .HasColumnType("uuid")
                .HasColumnName("tenant_id");

            b.Property<int>("AttemptCount")
                .ValueGeneratedOnAdd()
                .HasColumnType("integer")
                .HasDefaultValue(0)
                .HasColumnName("attempt_count");

            b.Property<Guid>("CorrelationId")
                .HasColumnType("uuid")
                .HasColumnName("correlation_id");

            b.Property<DateTimeOffset>("CreatedAtUtc")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("created_at_utc");

            b.Property<Guid?>("CreatedBy")
                .HasColumnType("uuid")
                .HasColumnName("created_by");

            b.Property<string>("LastErrorSummary")
                .HasMaxLength(1000)
                .HasColumnType("character varying(1000)")
                .HasColumnName("last_error_summary");

            b.Property<string>("MessageType")
                .IsRequired()
                .HasMaxLength(200)
                .HasColumnType("character varying(200)")
                .HasColumnName("message_type");

            b.Property<DateTimeOffset>("NextAttemptAtUtc")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("next_attempt_at_utc");

            b.Property<string>("PayloadJson")
                .IsRequired()
                .HasColumnType("jsonb")
                .HasColumnName("payload_json");

            b.Property<DateTimeOffset?>("ProcessedAtUtc")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("processed_at_utc");

            b.Property<string>("Status")
                .IsRequired()
                .HasMaxLength(24)
                .HasColumnType("character varying(24)")
                .HasColumnName("status");

            b.Property<DateTimeOffset?>("UpdatedAtUtc")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("updated_at_utc");

            b.Property<Guid?>("UpdatedBy")
                .HasColumnType("uuid")
                .HasColumnName("updated_by");

            b.Property<uint>("xmin")
                .IsConcurrencyToken()
                .ValueGeneratedOnAddOrUpdate()
                .HasColumnType("xid")
                .HasColumnName("xmin")
                .HasDefaultValue(0u);

            b.HasKey("Id");

            b.HasIndex("Status", "NextAttemptAtUtc");

            b.ToTable("infra_outbox_messages", (string)null);
        });

        modelBuilder.Entity("PeopleFlow.Persistence.Entities.StoredFile", b =>
        {
            b.Property<Guid>("Id")
                .ValueGeneratedOnAdd()
                .HasColumnType("uuid")
                .HasColumnName("id");

            b.Property<Guid>("TenantId")
                .HasColumnType("uuid")
                .HasColumnName("tenant_id");

            b.Property<string>("BucketName")
                .HasMaxLength(120)
                .HasColumnType("character varying(120)")
                .HasColumnName("bucket_name");

            b.Property<string>("ContentType")
                .IsRequired()
                .HasMaxLength(120)
                .HasColumnType("character varying(120)")
                .HasColumnName("content_type");

            b.Property<DateTimeOffset>("CreatedAtUtc")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("created_at_utc");

            b.Property<Guid?>("CreatedBy")
                .HasColumnType("uuid")
                .HasColumnName("created_by");

            b.Property<Guid?>("OwnerEntityId")
                .HasColumnType("uuid")
                .HasColumnName("owner_entity_id");

            b.Property<string>("OwnerEntityType")
                .IsRequired()
                .HasMaxLength(64)
                .HasColumnType("character varying(64)")
                .HasColumnName("owner_entity_type");

            b.Property<string>("OwnerModule")
                .IsRequired()
                .HasMaxLength(48)
                .HasColumnType("character varying(48)")
                .HasColumnName("owner_module");

            b.Property<string>("OriginalFileName")
                .IsRequired()
                .HasMaxLength(255)
                .HasColumnType("character varying(255)")
                .HasColumnName("original_file_name");

            b.Property<string>("RetentionCategory")
                .IsRequired()
                .HasMaxLength(48)
                .HasColumnType("character varying(48)")
                .HasColumnName("retention_category");

            b.Property<string>("ScanStatus")
                .IsRequired()
                .HasMaxLength(32)
                .HasColumnType("character varying(32)")
                .HasColumnName("scan_status");

            b.Property<DateTimeOffset?>("ScannedAtUtc")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("scanned_at_utc");

            b.Property<string>("SensitivityLevel")
                .IsRequired()
                .HasMaxLength(24)
                .HasColumnType("character varying(24)")
                .HasColumnName("sensitivity_level");

            b.Property<string>("Sha256Checksum")
                .HasMaxLength(64)
                .HasColumnType("character(64)")
                .IsFixedLength()
                .HasColumnName("sha256_checksum");

            b.Property<long>("SizeBytes")
                .HasColumnType("bigint")
                .HasColumnName("size_bytes");

            b.Property<string>("StorageKey")
                .IsRequired()
                .HasMaxLength(512)
                .HasColumnType("character varying(512)")
                .HasColumnName("storage_key");

            b.Property<DateTimeOffset?>("UpdatedAtUtc")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("updated_at_utc");

            b.Property<Guid?>("UpdatedBy")
                .HasColumnType("uuid")
                .HasColumnName("updated_by");

            b.Property<Guid?>("UploadedByUserId")
                .HasColumnType("uuid")
                .HasColumnName("uploaded_by_user_id");

            b.Property<uint>("xmin")
                .IsConcurrencyToken()
                .ValueGeneratedOnAddOrUpdate()
                .HasColumnType("xid")
                .HasColumnName("xmin")
                .HasDefaultValue(0u);

            b.HasKey("Id");

            b.HasAlternateKey("TenantId", "Id");

            b.ToTable("infra_stored_files", (string)null);
        });

        modelBuilder.Entity("PeopleFlow.Persistence.Entities.BackgroundJobRun", b =>
        {
            b.HasOne("PeopleFlow.Persistence.Entities.StoredFile", "ErrorReportFile")
                .WithMany()
                .HasForeignKey("TenantId", "ErrorReportFileId")
                .HasPrincipalKey("TenantId", "Id")
                .OnDelete(DeleteBehavior.SetNull);

            b.Navigation("ErrorReportFile");
        });
#pragma warning restore 612, 618
    }
}
