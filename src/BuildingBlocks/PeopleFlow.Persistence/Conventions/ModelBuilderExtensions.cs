using System.Linq.Expressions;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PeopleFlow.Kernel.Domain;
using PeopleFlow.Persistence.Context;
using PeopleFlow.Persistence.Entities;

namespace PeopleFlow.Persistence.Conventions;

public static class ModelBuilderExtensions
{
    /// <summary>
    /// Configures PostgreSQL system column 'xmin' as optimistic concurrency token on the entity.
    /// </summary>
    public static EntityTypeBuilder UseXminAsConcurrencyToken(this EntityTypeBuilder builder)
    {
        builder.Property<uint>("xmin").IsRowVersion().HasDefaultValue(0u);
        return builder;
    }

    /// <summary>
    /// Configures PostgreSQL system column 'xmin' as optimistic concurrency token on the entity.
    /// </summary>
    public static EntityTypeBuilder<TEntity> UseXminAsConcurrencyToken<TEntity>(this EntityTypeBuilder<TEntity> builder)
        where TEntity : class
    {
        builder.Property<uint>("xmin").IsRowVersion().HasDefaultValue(0u);
        return builder;
    }

    /// <summary>
    /// Applies global multi-tenancy query filters for all entities with a TenantId property.
    /// Directly compares nullable CurrentTenantId with TenantId without throwing when context is null.
    /// </summary>
    public static ModelBuilder ApplyTenantQueryFilters(this ModelBuilder modelBuilder, PeopleFlowDbContext context)
    {
        var contextConstant = Expression.Constant(context);
        var currentTenantIdProp = typeof(PeopleFlowDbContext).GetProperty(nameof(PeopleFlowDbContext.CurrentTenantId))!;
        var currentTenantIdAccess = Expression.Property(contextConstant, currentTenantIdProp); // context.CurrentTenantId (Guid?)

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var tenantProp = entityType.FindProperty("TenantId");
            if (tenantProp == null) continue;

            var parameter = Expression.Parameter(entityType.ClrType, "e");
            var entityTenantProp = Expression.Property(parameter, "TenantId");

            Expression convertedTenantProp = tenantProp.ClrType == typeof(Guid)
                ? Expression.Convert(entityTenantProp, typeof(Guid?))
                : entityTenantProp;

            // Direct comparison between Guid? and Guid?
            // When CurrentTenantId is null: (Guid?)e.TenantId == null evaluates to false (fail-closed, 0 rows)
            // When CurrentTenantId has tenant: (Guid?)e.TenantId == CurrentTenantId matches tenant rows
            var filterExpression = Expression.Equal(convertedTenantProp, currentTenantIdAccess);

            var lambda = Expression.Lambda(filterExpression, parameter);
            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(lambda);
        }

        return modelBuilder;
    }

    /// <summary>
    /// Configures PostgreSQL system column 'xmin' as optimistic concurrency token on BaseEntity instances.
    /// </summary>
    public static ModelBuilder ApplyConcurrencyTokens(this ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(BaseEntity).IsAssignableFrom(entityType.ClrType))
            {
                modelBuilder.Entity(entityType.ClrType).UseXminAsConcurrencyToken();
            }
        }

        return modelBuilder;
    }

    /// <summary>
    /// Configures infrastructure core entities with their schema, table names, keys, constraints, and composite foreign keys.
    /// </summary>
    public static ModelBuilder ApplyInfrastructureEntityConfigurations(this ModelBuilder modelBuilder)
    {
        // 1. IdempotencyRecord
        modelBuilder.Entity<IdempotencyRecord>(b =>
        {
            b.ToTable("infra_idempotency_records");
            b.HasKey(e => e.Id);
            b.Property(e => e.EndpointKey).HasMaxLength(160).IsRequired();
            b.Property(e => e.IdempotencyKey).HasMaxLength(128).IsRequired();
            b.Property(e => e.RequestHash).HasMaxLength(64).IsFixedLength().IsRequired();
            b.Property(e => e.Status).HasMaxLength(24).IsRequired();
            b.Property(e => e.ResponseBodyJson).HasColumnType("jsonb");
            b.HasIndex(e => new { e.TenantId, e.ActorUserId, e.EndpointKey, e.IdempotencyKey }).IsUnique();
            b.HasIndex(e => e.ExpiresAtUtc);

            // Ignore alias properties
            b.Ignore(e => e.Key);
            b.Ignore(e => e.ResponseBody);

            b.UseXminAsConcurrencyToken();
        });

        // 2. OutboxMessage
        modelBuilder.Entity<OutboxMessage>(b =>
        {
            b.ToTable("infra_outbox_messages");
            b.HasKey(e => e.Id);
            b.Property(e => e.MessageType).HasMaxLength(200).IsRequired();
            b.Property(e => e.PayloadJson).HasColumnType("jsonb").IsRequired();
            b.Property(e => e.Status).HasMaxLength(24).IsRequired();
            b.Property(e => e.AttemptCount).HasDefaultValue(0);
            b.Property(e => e.LastErrorSummary).HasMaxLength(1000);
            b.HasIndex(e => new { e.Status, e.NextAttemptAtUtc });

            // Ignore alias properties
            b.Ignore(e => e.EventType);
            b.Ignore(e => e.RetryCount);
            b.Ignore(e => e.LastError);

            b.UseXminAsConcurrencyToken();
        });

        // 3. StoredFile
        modelBuilder.Entity<StoredFile>(b =>
        {
            b.ToTable("infra_stored_files");
            b.HasKey(e => e.Id);
            b.HasAlternateKey(e => new { e.TenantId, e.Id }); // Composite key for cross-tenant isolation
            b.Property(e => e.OwnerModule).HasMaxLength(48).IsRequired();
            b.Property(e => e.OwnerEntityType).HasMaxLength(64).IsRequired();
            b.Property(e => e.SensitivityLevel).HasMaxLength(24).IsRequired();
            b.Property(e => e.OriginalFileName).HasMaxLength(255).IsRequired();
            b.Property(e => e.ContentType).HasMaxLength(120).IsRequired();
            b.Property(e => e.Sha256Checksum).HasMaxLength(64).IsFixedLength();
            b.Property(e => e.StorageKey).HasMaxLength(512).IsRequired();
            b.Property(e => e.BucketName).HasMaxLength(120);
            b.Property(e => e.ScanStatus).HasMaxLength(32).IsRequired();
            b.Property(e => e.RetentionCategory).HasMaxLength(48).IsRequired();

            // Ignore alias properties
            b.Ignore(e => e.FileSizeBytes);
            b.Ignore(e => e.ObjectKey);
            b.Ignore(e => e.Status);

            b.UseXminAsConcurrencyToken();
        });

        // 4. BackgroundJobRun
        modelBuilder.Entity<BackgroundJobRun>(b =>
        {
            b.ToTable("infra_background_job_runs");
            b.HasKey(e => e.Id);
            b.HasAlternateKey(e => new { e.TenantId, e.Id });
            b.Property(e => e.JobCategory).HasMaxLength(64).IsRequired();
            b.Property(e => e.HangfireJobId).HasMaxLength(64);
            b.Property(e => e.Status).HasMaxLength(32).IsRequired();
            b.Property(e => e.SucceededItems).HasDefaultValue(0);
            b.Property(e => e.FailedItems).HasDefaultValue(0);

            // Composite Foreign Key to StoredFile (tenant_id, error_report_file_id) -> (tenant_id, id)
            b.HasOne(e => e.ErrorReportFile)
                .WithMany()
                .HasForeignKey(e => new { e.TenantId, e.ErrorReportFileId })
                .HasPrincipalKey(e => new { e.TenantId, e.Id })
                .OnDelete(DeleteBehavior.SetNull);

            // Ignore alias properties
            b.Ignore(e => e.JobName);
            b.Ignore(e => e.ProgressPercentage);

            b.UseXminAsConcurrencyToken();
        });

        // 5. DataProtectionKeyRecord (global host key ring, no TenantId)
        modelBuilder.Entity<DataProtectionKeyRecord>(b =>
        {
            b.ToTable("infra_data_protection_keys");
            b.HasKey(e => e.Id);
            b.Property(e => e.FriendlyName).HasMaxLength(256);
            b.Property(e => e.Xml).HasColumnType("text").IsRequired();
            b.UseXminAsConcurrencyToken();
        });

        return modelBuilder;
    }

    /// <summary>
    /// Applies snake_case naming conventions to all entity properties in the model.
    /// Ensures EF Core query translation aligns with PostgreSQL schema and RLS policies.
    /// </summary>
    public static ModelBuilder ApplySnakeCaseNamingConvention(this ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                if (property.Name == "xmin")
                {
                    property.SetColumnName("xmin");
                    continue;
                }

                property.SetColumnName(ToSnakeCase(property.Name));
            }
        }

        return modelBuilder;
    }

    private static string ToSnakeCase(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        var sb = new StringBuilder();
        for (int i = 0; i < input.Length; i++)
        {
            var c = input[i];
            if (char.IsUpper(c))
            {
                if (i > 0 && input[i - 1] != '_')
                {
                    sb.Append('_');
                }
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Generates idempotent SQL statements for enabling PostgreSQL Row Level Security (RLS)
    /// and configuring fail-closed tenant isolation policies for all tenant entities in the model.
    /// Handles both non-nullable and nullable tenant_id columns.
    /// </summary>
    public static string GenerateRlsPolicySql(this IModel model, string roleName = "peopleflow_app")
    {
        var sb = new StringBuilder();
        var processedTables = new HashSet<string>();

        foreach (var entityType in model.GetEntityTypes())
        {
            var tenantProp = entityType.FindProperty("TenantId");
            if (tenantProp == null) continue;

            var tableName = entityType.GetTableName();
            if (string.IsNullOrEmpty(tableName) || !processedTables.Add(tableName))
            {
                continue;
            }

            var isNullable = tenantProp.IsNullable || tenantProp.ClrType == typeof(Guid?);
            var isOutbox = string.Equals(tableName, "infra_outbox_messages", StringComparison.OrdinalIgnoreCase);

            var rlsPredicate = isOutbox
                ? "tenant_id IS NULL OR tenant_id = app_current_tenant_id() OR app_is_outbox_dispatcher()"
                : (isNullable
                    ? "tenant_id IS NULL OR tenant_id = app_current_tenant_id()"
                    : "tenant_id = app_current_tenant_id()");

            sb.AppendLine($"ALTER TABLE {tableName} ENABLE ROW LEVEL SECURITY;");
            sb.AppendLine($"ALTER TABLE {tableName} FORCE ROW LEVEL SECURITY;");
            sb.AppendLine();
            sb.AppendLine($"DO $$");
            sb.AppendLine($"BEGIN");
            sb.AppendLine($"    IF NOT EXISTS (");
            sb.AppendLine($"        SELECT 1 FROM pg_policies ");
            sb.AppendLine($"        WHERE tablename = '{tableName}' AND policyname = 'tenant_isolation_policy'");
            sb.AppendLine($"    ) THEN");
            sb.AppendLine($"        CREATE POLICY tenant_isolation_policy ON {tableName}");
            sb.AppendLine($"            AS PERMISSIVE");
            sb.AppendLine($"            FOR ALL");
            sb.AppendLine($"            TO {roleName}");
            sb.AppendLine($"            USING ({rlsPredicate})");
            sb.AppendLine($"            WITH CHECK ({rlsPredicate});");
            sb.AppendLine($"    END IF;");
            sb.AppendLine($"END $$;");
            sb.AppendLine();
        }

        return sb.ToString();
    }
}
