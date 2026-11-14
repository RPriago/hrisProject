using Microsoft.EntityFrameworkCore;
using PeopleFlow.Kernel.Time;
using PeopleFlow.Modules.Tenancy.Core.Entities;
using PeopleFlow.MultiTenancy.Abstractions;
using PeopleFlow.Persistence.Context;
using PeopleFlow.Security.Context;

namespace PeopleFlow.Modules.Tenancy.Core.Persistence;

public class TenancyDbContext : PeopleFlowDbContext
{
    public TenancyDbContext(
        DbContextOptions<TenancyDbContext> options,
        ITenantContextAccessor? tenantContextAccessor = null,
        IClock? clock = null,
        ICurrentActorContext? currentActorContext = null)
        : base(options, tenantContextAccessor, clock, currentActorContext)
    {
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<SupportAccessGrant> SupportAccessGrants => Set<SupportAccessGrant>();
    public DbSet<TenancyMembership> TenancyMemberships => Set<TenancyMembership>();
    public DbSet<TenancyInvitation> TenancyInvitations => Set<TenancyInvitation>();
    public DbSet<TenancyRole> TenancyRoles => Set<TenancyRole>();
    public DbSet<TenancyRolePermission> TenancyRolePermissions => Set<TenancyRolePermission>();
    public DbSet<TenancyMemberRoleScope> TenancyMemberRoleScopes => Set<TenancyMemberRoleScope>();
    public DbSet<ApiIntegrationClient> ApiIntegrationClients => Set<ApiIntegrationClient>();

    protected override void ConfigureModuleModels(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tenant>(b =>
        {
            b.ToTable("tenancy_tenants");
            b.HasKey(e => e.Id);
            b.Property(e => e.Slug).HasMaxLength(64).IsRequired();
            b.Property(e => e.DisplayName).HasMaxLength(200).IsRequired();
            b.Property(e => e.IndustryPresetCode).HasMaxLength(48).IsRequired();
            b.Property(e => e.DefaultLanguage).HasMaxLength(10).IsRequired();
            b.Property(e => e.DefaultTimeZoneId).HasMaxLength(64).IsRequired();
            b.Property(e => e.Status).HasMaxLength(24).IsRequired();
            b.HasIndex(e => e.Slug).IsUnique();
        });

        modelBuilder.Entity<SupportAccessGrant>(b =>
        {
            b.ToTable("tenancy_support_access_grants");
            b.HasKey(e => e.Id);
            b.Property(e => e.TicketReference).HasMaxLength(80).IsRequired();
            b.Property(e => e.Reason).HasMaxLength(500).IsRequired();
            b.Property(e => e.AccessLevel).HasMaxLength(40).IsRequired();
            b.HasIndex(e => new { e.TenantId, e.RevokedAtUtc, e.ExpiresAtUtc });
        });

        modelBuilder.Entity<TenancyMembership>(b =>
        {
            b.ToTable("tenancy_memberships");
            b.HasKey(e => e.Id);
            b.HasAlternateKey(e => new { e.TenantId, e.Id });
            b.Property(e => e.Status).HasMaxLength(24).IsRequired();
            b.Property(e => e.RevokeReason).HasMaxLength(120);
            b.HasIndex(e => new { e.TenantId, e.UserId }).IsUnique();
        });

        modelBuilder.Entity<TenancyInvitation>(b =>
        {
            b.ToTable("tenancy_invitations");
            b.HasKey(e => e.Id);
            b.Property(e => e.InvitedEmail).HasMaxLength(256).IsRequired();
            b.Property(e => e.InitialRoleAssignmentsJson).HasColumnType("jsonb").IsRequired();
            b.Property(e => e.TokenHash).HasMaxLength(64).IsFixedLength().IsRequired();
            b.Property(e => e.Status).HasMaxLength(24).IsRequired();
            b.HasIndex(e => e.TokenHash).IsUnique();
        });

        modelBuilder.Entity<TenancyRole>(b =>
        {
            b.ToTable("tenancy_roles");
            b.HasKey(e => e.Id);
            b.HasAlternateKey(e => new { e.TenantId, e.Id });
            b.Property(e => e.Code).HasMaxLength(64).IsRequired();
            b.Property(e => e.Name).HasMaxLength(120).IsRequired();
            b.Property(e => e.Description).HasMaxLength(400);
            b.HasIndex(e => new { e.TenantId, e.Code }).IsUnique();
        });

        modelBuilder.Entity<TenancyRolePermission>(b =>
        {
            b.ToTable("tenancy_role_permissions");
            b.HasKey(e => new { e.TenantId, e.RoleId, e.PermissionCode });
            b.Property(e => e.PermissionCode).HasMaxLength(100).IsRequired();
            b.HasOne<TenancyRole>()
                .WithMany()
                .HasPrincipalKey(r => new { r.TenantId, r.Id })
                .HasForeignKey(p => new { p.TenantId, p.RoleId })
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TenancyMemberRoleScope>(b =>
        {
            b.ToTable("tenancy_member_role_scopes");
            b.HasKey(e => e.Id);
            b.Property(e => e.ScopeType).HasMaxLength(24).IsRequired();
            b.Property(e => e.ScopeTargetIdsJson).HasColumnType("jsonb");
            b.HasOne<TenancyMembership>()
                .WithMany()
                .HasPrincipalKey(m => new { m.TenantId, m.Id })
                .HasForeignKey(s => new { s.TenantId, s.MembershipId })
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne<TenancyRole>()
                .WithMany()
                .HasPrincipalKey(r => new { r.TenantId, r.Id })
                .HasForeignKey(s => new { s.TenantId, s.RoleId })
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ApiIntegrationClient>(b =>
        {
            b.ToTable("tenancy_api_integration_clients");
            b.HasKey(e => e.Id);
            b.Property(e => e.Name).HasMaxLength(120).IsRequired();
            b.Property(e => e.ClientId).HasMaxLength(80).IsRequired();
            b.Property(e => e.ActiveSecretHash).HasMaxLength(255).IsRequired();
            b.Property(e => e.PreviousSecretHash).HasMaxLength(255);
            b.Property(e => e.AllowedScopesJson).HasColumnType("jsonb").IsRequired();
            b.Property(e => e.ScopedLegalEntityIdsJson).HasColumnType("jsonb");
            b.Property(e => e.ScopedLocationIdsJson).HasColumnType("jsonb");
            b.HasIndex(e => e.ClientId).IsUnique();
        });
    }
}
