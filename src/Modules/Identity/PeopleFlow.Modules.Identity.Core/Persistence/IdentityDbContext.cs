using Microsoft.EntityFrameworkCore;
using PeopleFlow.Kernel.Time;
using PeopleFlow.Modules.Identity.Core.Entities;
using PeopleFlow.MultiTenancy.Abstractions;
using PeopleFlow.Persistence.Context;
using PeopleFlow.Security.Context;

namespace PeopleFlow.Modules.Identity.Core.Persistence;

public class IdentityDbContext : PeopleFlowDbContext
{
    public IdentityDbContext(
        DbContextOptions<IdentityDbContext> options,
        ITenantContextAccessor? tenantContextAccessor = null,
        IClock? clock = null,
        ICurrentActorContext? currentActorContext = null)
        : base(options, tenantContextAccessor, clock, currentActorContext)
    {
    }

    public DbSet<IdentityUser> IdentityUsers => Set<IdentityUser>();
    public DbSet<IdentityUserSession> IdentityUserSessions => Set<IdentityUserSession>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();

    protected override void ConfigureModuleModels(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IdentityUser>(b =>
        {
            b.ToTable("identity_users");
            b.HasKey(e => e.Id);
            b.Property(e => e.Email).HasMaxLength(256).IsRequired();
            b.Property(e => e.NormalizedEmail).HasMaxLength(256).IsRequired();
            b.Property(e => e.PasswordHash).HasMaxLength(512).IsRequired();
            b.Property(e => e.AuthenticatorKeyEncrypted).HasMaxLength(512);
            b.Property(e => e.PendingAuthenticatorKeyEncrypted).HasMaxLength(512);
            b.Property(e => e.PendingAuthenticatorKeyExpiresAtUtc);
            b.Property(e => e.LastTotpStepUsed);
            b.Property(e => e.RecoveryCodesHashJson).HasColumnType("jsonb");
            b.Property(e => e.SecurityStamp).HasMaxLength(64).IsRequired();
            b.Property(e => e.PreferredLanguage).HasMaxLength(10).IsRequired();
            b.HasIndex(e => e.NormalizedEmail).IsUnique();
        });

        modelBuilder.Entity<IdentityUserSession>(b =>
        {
            b.ToTable("identity_user_sessions");
            b.HasKey(e => e.Id);
            b.Property(e => e.IpAddressMasked).HasMaxLength(64);
            b.Property(e => e.UserAgentSummary).HasMaxLength(255);
            b.Property(e => e.RevokeReason).HasMaxLength(64);
            b.HasIndex(e => new { e.UserId, e.RevokedAtUtc });
            b.HasIndex(e => new { e.ActiveTenantId, e.UserId });
        });

        modelBuilder.Entity<PasswordResetToken>(b =>
        {
            b.ToTable("identity_password_reset_tokens");
            b.HasKey(e => e.Id);
            b.Property(e => e.TokenHash).HasMaxLength(64).IsFixedLength().IsRequired();
            b.HasIndex(e => e.TokenHash).IsUnique();
        });
    }
}
