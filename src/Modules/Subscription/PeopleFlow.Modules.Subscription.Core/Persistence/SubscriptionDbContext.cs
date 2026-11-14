using Microsoft.EntityFrameworkCore;
using PeopleFlow.Kernel.Time;
using PeopleFlow.Modules.Subscription.Core.Entities;
using PeopleFlow.MultiTenancy.Abstractions;
using PeopleFlow.Persistence.Context;
using PeopleFlow.Security.Context;

namespace PeopleFlow.Modules.Subscription.Core.Persistence;

public class SubscriptionDbContext : PeopleFlowDbContext
{
    public SubscriptionDbContext(
        DbContextOptions<SubscriptionDbContext> options,
        ITenantContextAccessor? tenantContextAccessor = null,
        IClock? clock = null,
        ICurrentActorContext? currentActorContext = null)
        : base(options, tenantContextAccessor, clock, currentActorContext)
    {
    }

    public DbSet<SubscriptionPlan> SubscriptionPlans => Set<SubscriptionPlan>();
    public DbSet<TenantSubscription> TenantSubscriptions => Set<TenantSubscription>();
    public DbSet<SubscriptionInvoice> SubscriptionInvoices => Set<SubscriptionInvoice>();
    public DbSet<BillingWebhookEvent> BillingWebhookEvents => Set<BillingWebhookEvent>();

    protected override void ConfigureModuleModels(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SubscriptionPlan>(b =>
        {
            b.ToTable("subscription_plans");
            b.HasKey(e => e.Id);
            b.Property(e => e.Code).HasMaxLength(48).IsRequired();
            b.Property(e => e.Name).HasMaxLength(120).IsRequired();
            b.Property(e => e.EnabledModulesJson).HasColumnType("jsonb").IsRequired();
            b.Property(e => e.Currency).HasMaxLength(3).IsRequired();
            b.Property(e => e.MonthlyPriceAmount).HasPrecision(19, 2);
            b.HasIndex(e => e.Code).IsUnique();
        });

        modelBuilder.Entity<TenantSubscription>(b =>
        {
            b.ToTable("subscription_tenant_subscriptions");
            b.HasKey(e => e.Id);
            b.Property(e => e.Status).HasMaxLength(24).IsRequired();
            b.HasIndex(e => e.TenantId).IsUnique();
        });

        modelBuilder.Entity<SubscriptionInvoice>(b =>
        {
            b.ToTable("subscription_invoices");
            b.HasKey(e => e.Id);
            b.Property(e => e.InvoiceNumber).HasMaxLength(64).IsRequired();
            b.Property(e => e.Amount).HasPrecision(19, 2);
            b.Property(e => e.Currency).HasMaxLength(3).IsRequired();
            b.Property(e => e.Status).HasMaxLength(24).IsRequired();
            b.Property(e => e.PaymentChannel).HasMaxLength(32).IsRequired();
            b.Property(e => e.PaymentReference).HasMaxLength(160);
            b.Property(e => e.Notes).HasMaxLength(500);
            b.HasIndex(e => e.InvoiceNumber).IsUnique();
        });

        modelBuilder.Entity<BillingWebhookEvent>(b =>
        {
            b.ToTable("subscription_billing_webhook_events");
            b.HasKey(e => e.Id);
            b.Property(e => e.ProviderName).HasMaxLength(48).IsRequired();
            b.Property(e => e.ProviderEventId).HasMaxLength(160).IsRequired();
            b.Property(e => e.EventType).HasMaxLength(100).IsRequired();
            b.Property(e => e.PayloadJson).HasColumnType("jsonb").IsRequired();
            b.Property(e => e.ProcessingStatus).HasMaxLength(32).IsRequired();
            b.Property(e => e.ErrorSummary).HasMaxLength(1000);
            b.HasIndex(e => new { e.ProviderName, e.ProviderEventId }).IsUnique();
        });
    }
}
