using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using PeopleFlow.Kernel.Domain;
using PeopleFlow.Kernel.Time;
using PeopleFlow.MultiTenancy.Abstractions;
using PeopleFlow.MultiTenancy.Interceptors;
using PeopleFlow.Persistence.Conventions;
using PeopleFlow.Persistence.Entities;
using PeopleFlow.Security.Context;

namespace PeopleFlow.Persistence.Context;

/// <summary>
/// Base DbContext for PeopleFlow SaaS HRIS.
/// Configures multi-tenant RLS interceptors, global query filters, optimistic concurrency (xmin),
/// composite tenant foreign keys, UUIDv7 generation, and automatic audit timestamps.
/// </summary>
public class PeopleFlowDbContext : DbContext
{
    private readonly ITenantContextAccessor? _tenantContextAccessor;
    private readonly IClock? _clock;
    private readonly ICurrentActorContext? _currentActorContext;
    private readonly NpgsqlTenantRlsInterceptor? _rlsInterceptor;

    public Guid? CurrentTenantId => _tenantContextAccessor?.TenantId;

    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<BackgroundJobRun> BackgroundJobRuns => Set<BackgroundJobRun>();
    public DbSet<StoredFile> StoredFiles => Set<StoredFile>();

    public PeopleFlowDbContext(
        DbContextOptions<PeopleFlowDbContext> options,
        ITenantContextAccessor? tenantContextAccessor = null,
        IClock? clock = null,
        ICurrentActorContext? currentActorContext = null)
        : base(options)
    {
        _tenantContextAccessor = tenantContextAccessor;
        _clock = clock ?? new SystemClock();
        _currentActorContext = currentActorContext;
        _rlsInterceptor = tenantContextAccessor != null
            ? new NpgsqlTenantRlsInterceptor(tenantContextAccessor)
            : null;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);

        if (_rlsInterceptor != null)
        {
            var alreadyRegistered = optionsBuilder.Options
                .FindExtension<CoreOptionsExtension>()
                ?.Interceptors?
                .OfType<NpgsqlTenantRlsInterceptor>()
                .Any() ?? false;

            if (!alreadyRegistered)
            {
                optionsBuilder.AddInterceptors(_rlsInterceptor);
            }
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureModuleModels(modelBuilder);

        modelBuilder.ApplyInfrastructureEntityConfigurations();
        modelBuilder.ApplyConcurrencyTokens();
        modelBuilder.ApplyTenantQueryFilters(this);
        modelBuilder.ApplySnakeCaseNamingConvention();
    }

    /// <summary>
    /// Extension point for downstream domain modules (Modul 01B, 02, etc.) to register domain entities,
    /// relationships, and module-specific configurations before multi-tenancy, concurrency tokens,
    /// and snake_case naming conventions are applied.
    /// </summary>
    protected virtual void ConfigureModuleModels(ModelBuilder modelBuilder)
    {
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ValidateTenantScope();
        UpdateAuditTimestamps();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        ValidateTenantScope();
        UpdateAuditTimestamps();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Enforces fail-closed multi-tenant boundary at the ORM unit-of-work level.
    /// Prohibits empty TenantId, mutations without active tenant context, and cross-tenant entity modifications.
    /// </summary>
    private void ValidateTenantScope()
    {
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State != EntityState.Added && entry.State != EntityState.Modified && entry.State != EntityState.Deleted)
            {
                continue;
            }

            if (entry.State == EntityState.Modified)
            {
                var incMethod = entry.Entity.GetType().GetMethod("IncrementVersion", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (incMethod != null && incMethod.GetParameters().Length == 0)
                {
                    incMethod.Invoke(entry.Entity, null);
                }
            }

            var tenantProp = entry.Properties.FirstOrDefault(p => p.Metadata.Name == "TenantId");
            if (tenantProp == null)
            {
                continue;
            }

            if (entry.Entity is OutboxMessage)
            {
                if (tenantProp.CurrentValue is Guid outboxTenantId)
                {
                    if (outboxTenantId == Guid.Empty)
                    {
                        throw new InvalidOperationException(
                            $"Cannot persist entity '{entry.Metadata.Name}' with empty TenantId.");
                    }

                    if (_tenantContextAccessor != null && _tenantContextAccessor.TenantId.HasValue && _tenantContextAccessor.TenantId.Value != outboxTenantId)
                    {
                        throw new InvalidOperationException(
                            $"Cross-tenant mutation prohibited: Entity TenantId '{outboxTenantId}' does not match active context TenantId '{_tenantContextAccessor.TenantId.Value}'.");
                    }
                }
                continue;
            }

            if (tenantProp.CurrentValue is Guid tenantId)
            {
                if (tenantId == Guid.Empty)
                {
                    throw new InvalidOperationException(
                        $"Cannot persist entity '{entry.Metadata.Name}' with empty TenantId.");
                }

                if (_tenantContextAccessor != null)
                {
                    if (!_tenantContextAccessor.CurrentTenant.HasTenant || !_tenantContextAccessor.TenantId.HasValue)
                    {
                        throw new InvalidOperationException(
                            $"Cannot persist entity '{entry.Metadata.Name}' without an active tenant context.");
                    }

                    if (_tenantContextAccessor.TenantId.Value != tenantId)
                    {
                        throw new InvalidOperationException(
                            $"Cross-tenant mutation prohibited: Entity TenantId '{tenantId}' does not match active context TenantId '{_tenantContextAccessor.TenantId.Value}'.");
                    }
                }
            }
        }
    }

    /// <summary>
    /// Updates audit timestamps and generates UUIDv7 keys for tracked entries.
    /// Sets CreatedAtUtc to the active clock time on Added, and UpdatedAtUtc on Modified.
    /// </summary>
    public void UpdateAuditTimestamps()
    {
        var now = (_clock ?? new SystemClock()).UtcNow;
        var actorUserId = (_currentActorContext is { IsAuthenticated: true } && _currentActorContext.UserId.HasValue && _currentActorContext.UserId.Value != Guid.Empty)
            ? _currentActorContext.UserId
            : (Guid?)null;

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is BaseEntity entity)
            {
                if (entry.State == EntityState.Added)
                {
                    if (entity.Id == Guid.Empty)
                    {
                        var prop = entry.Property(nameof(BaseEntity.Id));
                        prop.CurrentValue = Guid.CreateVersion7();
                    }

                    if (!entity.HasExplicitCreatedAtUtc)
                    {
                        entry.Property(nameof(BaseEntity.CreatedAtUtc)).CurrentValue = now;
                    }
                    entry.Property(nameof(BaseEntity.UpdatedAtUtc)).CurrentValue = null;

                    if (actorUserId.HasValue && entity.CreatedBy == null)
                    {
                        entry.Property(nameof(BaseEntity.CreatedBy)).CurrentValue = actorUserId.Value;
                    }
                }
                else if (entry.State == EntityState.Modified)
                {
                    entry.Property(nameof(BaseEntity.UpdatedAtUtc)).CurrentValue = now;

                    if (actorUserId.HasValue)
                    {
                        entry.Property(nameof(BaseEntity.UpdatedBy)).CurrentValue = actorUserId.Value;
                    }
                }
            }
            else
            {
                // Fallback for non-BaseEntity types that follow standard naming conventions
                if (entry.State == EntityState.Added)
                {
                    var idProp = entry.Properties.FirstOrDefault(p => p.Metadata.Name == "Id");
                    if (idProp != null && idProp.Metadata.ClrType == typeof(Guid) && ((Guid)idProp.CurrentValue! == Guid.Empty))
                    {
                        idProp.CurrentValue = Guid.CreateVersion7();
                    }

                    var createdProp = entry.Properties.FirstOrDefault(p => p.Metadata.Name == "CreatedAtUtc");
                    if (createdProp != null && createdProp.Metadata.ClrType == typeof(DateTimeOffset))
                    {
                        createdProp.CurrentValue = now;
                    }

                    var updatedProp = entry.Properties.FirstOrDefault(p => p.Metadata.Name == "UpdatedAtUtc");
                    if (updatedProp != null)
                    {
                        updatedProp.CurrentValue = null;
                    }

                    var createdByProp = entry.Properties.FirstOrDefault(p => p.Metadata.Name == "CreatedBy");
                    if (createdByProp != null && actorUserId.HasValue)
                    {
                        var currentCreatedBy = (Guid?)createdByProp.CurrentValue;
                        if (currentCreatedBy == null || currentCreatedBy == Guid.Empty)
                        {
                            createdByProp.CurrentValue = actorUserId.Value;
                        }
                    }
                }
                else if (entry.State == EntityState.Modified)
                {
                    var updatedProp = entry.Properties.FirstOrDefault(p => p.Metadata.Name == "UpdatedAtUtc");
                    if (updatedProp != null)
                    {
                        updatedProp.CurrentValue = now;
                    }

                    var updatedByProp = entry.Properties.FirstOrDefault(p => p.Metadata.Name == "UpdatedBy");
                    if (updatedByProp != null && actorUserId.HasValue)
                    {
                        updatedByProp.CurrentValue = actorUserId.Value;
                    }
                }
            }
        }
    }
}
