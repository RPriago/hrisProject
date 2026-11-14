using Hangfire;
using Hangfire.Common;
using Hangfire.Server;
using PeopleFlow.Jobs.Envelopes;
using PeopleFlow.MultiTenancy.Abstractions;
using PeopleFlow.MultiTenancy.Context;
using PeopleFlow.Security.Context;

namespace PeopleFlow.Jobs.Filters;

/// <summary>
/// Hangfire server filter that enforces multi-tenant isolation and payload hygiene.
/// Sets ambient TenantContext before execution and clears it on completion.
/// Rejects jobs without a JobEnvelope or with raw sensitive arguments, and aborts suspended tenants.
/// </summary>
public sealed class TenantJobActivationFilter : JobFilterAttribute, IServerFilter
{
    private static readonly HashSet<string> AllowedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Active",
        "Trial"
    };

    private readonly ITenantContextAccessor _tenantContextAccessor;
    private readonly ITenantSubscriptionValidator? _subscriptionValidator;
    private readonly ICurrentActorContext? _currentActorContext;

    public TenantJobActivationFilter(
        ITenantContextAccessor tenantContextAccessor,
        ITenantSubscriptionValidator? subscriptionValidator = null,
        ICurrentActorContext? currentActorContext = null)
    {
        _tenantContextAccessor = tenantContextAccessor ?? throw new ArgumentNullException(nameof(tenantContextAccessor));
        _subscriptionValidator = subscriptionValidator;
        _currentActorContext = currentActorContext;
    }

    public void OnPerforming(PerformingContext filterContext)
    {
        ArgumentNullException.ThrowIfNull(filterContext);

        var method = filterContext.BackgroundJob?.Job?.Method;
        var isPlatformJob = method != null &&
            (method.IsDefined(typeof(PlatformSystemJobAttribute), inherit: true) ||
             (method.DeclaringType?.IsDefined(typeof(PlatformSystemJobAttribute), inherit: true) ?? false));

        if (isPlatformJob)
        {
            var platformArgs = filterContext.BackgroundJob?.Job?.Args;
            if (platformArgs != null)
            {
                foreach (var arg in platformArgs)
                {
                    if (arg is CancellationToken || arg is IJobCancellationToken || arg is PerformContext || arg is PlatformJobEnvelope)
                    {
                        continue;
                    }

                    CancelAndClear(filterContext);
                    return;
                }
            }

            _tenantContextAccessor.Clear();
            _currentActorContext?.Clear();
            return;
        }

        JobEnvelope? envelope = null;
        var args = filterContext.BackgroundJob?.Job?.Args;

        if (args != null)
        {
            foreach (var arg in args)
            {
                if (arg is JobEnvelope env)
                {
                    if (envelope != null)
                    {
                        // Invariant: exactly one JobEnvelope argument allowed
                        CancelAndClear(filterContext);
                        return;
                    }
                    envelope = env;
                }
                else if (arg is CancellationToken || arg is IJobCancellationToken || arg is PerformContext)
                {
                    // Allowed framework infrastructure arguments
                    continue;
                }
                else
                {
                    // Invariant: NO passwords, PII, raw strings, file blobs or primitive arguments
                    CancelAndClear(filterContext);
                    return;
                }
            }
        }

        if (envelope == null || envelope.TenantId == Guid.Empty)
        {
            CancelAndClear(filterContext);
            return;
        }

        // Fail-closed: subscription validator is mandatory to prevent unvalidated tenant execution
        if (_subscriptionValidator == null)
        {
            CancelAndClear(filterContext);
            return;
        }

        if (!_subscriptionValidator.IsTenantExecutionAllowed(envelope.TenantId))
        {
            CancelAndClear(filterContext);
            return;
        }

        var reportedStatus = _subscriptionValidator.GetSubscriptionStatus(envelope.TenantId);
        if (string.IsNullOrWhiteSpace(reportedStatus) || !AllowedStatuses.Contains(reportedStatus))
        {
            CancelAndClear(filterContext);
            return;
        }

        // Activate ambient tenant context with verified active subscription status
        _tenantContextAccessor.SetTenantContext(new TenantContext(envelope.TenantId, reportedStatus, false));

        if (_currentActorContext != null)
        {
            var hasActor = envelope.ActorUserId.HasValue && envelope.ActorUserId.Value != Guid.Empty;
            _currentActorContext.SetActorContext(new ActorContext(
                UserId: hasActor ? envelope.ActorUserId : null,
                SessionId: null,
                Roles: new HashSet<string>(),
                CorrelationId: envelope.CorrelationId,
                IsAuthenticated: hasActor));
        }
    }

    public void OnPerformed(PerformedContext filterContext)
    {
        // Fail-closed: clear ambient tenant context to avoid cross-tenant worker thread pollution
        _tenantContextAccessor.Clear();
        _currentActorContext?.Clear();
    }

    private void CancelAndClear(PerformingContext context)
    {
        context.Canceled = true;
        _tenantContextAccessor.Clear();
        _currentActorContext?.Clear();
    }
}
