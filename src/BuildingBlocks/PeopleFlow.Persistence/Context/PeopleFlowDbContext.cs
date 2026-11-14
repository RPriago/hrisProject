using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using PeopleFlow.Kernel.Domain;
using PeopleFlow.Kernel.Time;
using PeopleFlow.MultiTenancy.Abstractions;
using PeopleFlow.MultiTenancy.Context;
using PeopleFlow.MultiTenancy.Interceptors;
using PeopleFlow.Persistence.Audit;
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
    private readonly AsyncLocal<Guid?> _scopedTenantOverride = new();

    public Guid? CurrentTenantId => _tenantContextAccessor?.TenantId ?? _scopedTenantOverride.Value;

    /// <summary>
    /// Opens a fail-closed operation-scoped tenant context for explicit-tenant operations
    /// (such as background job execution, quarantine scanning, or idempotency writes).
    /// Throws <see cref="UnauthorizedAccessException"/> if <paramref name="tenantId"/> is empty, and
    /// <see cref="InvalidOperationException"/> if a different tenant context is already active.
    /// </summary>
    public IDisposable BeginOperationTenantScope(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new UnauthorizedAccessException("Active tenant context with non-empty TenantId is required.");
        }

        if (CurrentTenantId.HasValue && CurrentTenantId.Value != tenantId)
        {
            throw new InvalidOperationException(
                "Cannot switch tenant scope to a different tenant while an active tenant context is present.");
        }

        if (_tenantContextAccessor != null)
        {
            return _tenantContextAccessor.BeginScope(new TenantContext(tenantId, "Active"));
        }

        var previous = _scopedTenantOverride.Value;
        _scopedTenantOverride.Value = tenantId;
        return new DelegatedScope(() => _scopedTenantOverride.Value = previous);
    }

    private sealed class DelegatedScope(Action onDispose) : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                onDispose();
            }
        }
    }

    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<BackgroundJobRun> BackgroundJobRuns => Set<BackgroundJobRun>();
    public DbSet<StoredFile> StoredFiles => Set<StoredFile>();
    public DbSet<DataProtectionKeyRecord> DataProtectionKeys => Set<DataProtectionKeyRecord>();
    public DbSet<PlatformAuditLog> PlatformAuditLogs => Set<PlatformAuditLog>();

    public PeopleFlowDbContext(
        DbContextOptions<PeopleFlowDbContext> options,
        ITenantContextAccessor? tenantContextAccessor = null,
        IClock? clock = null,
        ICurrentActorContext? currentActorContext = null)
        : this((DbContextOptions)options, tenantContextAccessor, clock, currentActorContext)
    {
    }

    protected PeopleFlowDbContext(
        DbContextOptions options,
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

        if (GetType() == typeof(PeopleFlowDbContext))
        {
            modelBuilder.ApplyInfrastructureEntityConfigurations();
        }
        else
        {
            modelBuilder.Ignore<IdempotencyRecord>();
            modelBuilder.Ignore<OutboxMessage>();
            modelBuilder.Ignore<BackgroundJobRun>();
            modelBuilder.Ignore<StoredFile>();
            modelBuilder.Ignore<DataProtectionKeyRecord>();
            modelBuilder.Ignore<PlatformAuditLog>();
        }
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

            if (entry.Entity is IPlatformOwnedEntity)
            {
                continue;
            }

            var tenantProp = entry.Properties.FirstOrDefault(p => p.Metadata.Name == "TenantId");
            if (tenantProp == null)
            {
                continue;
            }

            var activeTenantId = CurrentTenantId;
            var hasActiveTenant = activeTenantId.HasValue;

            if (entry.Entity is OutboxMessage)
            {
                if (tenantProp.CurrentValue is Guid outboxTenantId)
                {
                    if (outboxTenantId == Guid.Empty)
                    {
                        throw new InvalidOperationException(
                            $"Cannot persist entity '{entry.Metadata.Name}' with empty TenantId.");
                    }

                    if (hasActiveTenant && activeTenantId!.Value != outboxTenantId)
                    {
                        throw new InvalidOperationException(
                            $"Cross-tenant mutation prohibited: Entity TenantId '{outboxTenantId}' does not match active context TenantId '{activeTenantId.Value}'.");
                    }
                }
                else if (hasActiveTenant)
                {
                    throw new UnauthorizedAccessException(
                        $"Active tenant context '{activeTenantId}' cannot persist or modify platform-level (null TenantId) OutboxMessage rows.");
                }

                if ((entry.State == EntityState.Modified || entry.State == EntityState.Deleted) && hasActiveTenant)
                {
                    if (tenantProp.OriginalValue is not Guid origOutboxTenant || origOutboxTenant != activeTenantId!.Value)
                    {
                        throw new InvalidOperationException(
                            $"Cross-tenant mutation prohibited: Original TenantId '{tenantProp.OriginalValue}' does not match active context TenantId '{activeTenantId}'.");
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

                if (_tenantContextAccessor != null && !hasActiveTenant)
                {
                    throw new InvalidOperationException(
                        $"Cannot persist entity '{entry.Metadata.Name}' without an active tenant context.");
                }

                if (hasActiveTenant && activeTenantId!.Value != tenantId)
                {
                    throw new InvalidOperationException(
                        $"Cross-tenant mutation prohibited: Entity TenantId '{tenantId}' does not match active context TenantId '{activeTenantId.Value}'.");
                }

                if ((entry.State == EntityState.Modified || entry.State == EntityState.Deleted) && hasActiveTenant)
                {
                    if (tenantProp.OriginalValue is not Guid origTenantId || origTenantId != activeTenantId!.Value)
                    {
                        throw new InvalidOperationException(
                            $"Cross-tenant mutation prohibited: Original TenantId '{tenantProp.OriginalValue}' does not match active context TenantId '{activeTenantId}'.");
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
