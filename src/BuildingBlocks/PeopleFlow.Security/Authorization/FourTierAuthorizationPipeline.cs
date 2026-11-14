using PeopleFlow.Kernel.Common;

namespace PeopleFlow.Security.Authorization;

/// <summary>
/// Evaluates authorization requests across the 4 server-side security tiers:
/// Tier 1: Authentication & Identity
/// Tier 2: Tenant Lifecycle & Subscription Status
/// Tier 3: Feature / Plan Entitlement & Quotas
/// Tier 4: RBAC, Fine-grained Object Scope & Segregation of Duties
/// </summary>
public sealed class FourTierAuthorizationPipeline : IAuthorizationGuard
{
    private static readonly HashSet<string> ActiveStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Active",
        "Trial"
    };

    private static readonly HashSet<string> TerminatedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Suspended",
        "Cancelled",
        "Terminated"
    };

    public Task<Result> AuthorizeAsync(AuthorizationContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Tier 1: Authentication & Identity
        var tier1Result = EvaluateTier1(context.User);
        if (tier1Result.IsFailure)
        {
            return Task.FromResult(tier1Result);
        }

        // Tier 2: Tenant Lifecycle & Status
        var tier2Result = EvaluateTier2(context.Tenant, context.Requirement);
        if (tier2Result.IsFailure)
        {
            return Task.FromResult(tier2Result);
        }

        // Tier 3: Feature Entitlements & Quotas
        var tier3Result = EvaluateTier3(context.Tenant, context.Requirement);
        if (tier3Result.IsFailure)
        {
            return Task.FromResult(tier3Result);
        }

        // Tier 4: RBAC, Object Scope, & Segregation of Duties
        var tier4Result = EvaluateTier4(context.User, context.Requirement, context.Subject);
        if (tier4Result.IsFailure)
        {
            return Task.FromResult(tier4Result);
        }

        return Task.FromResult(Result.Success());
    }

    private static Result EvaluateTier1(AuthIdentity user)
    {
        if (!user.IsAuthenticated)
        {
            return Result.Failure(Error.Unauthorized("AUTH_REQUIRED", "User is not authenticated."));
        }

        if (!user.IsActive)
        {
            return Result.Failure(Error.Unauthorized("ACCOUNT_INACTIVE", "User account is deactivated."));
        }

        if (user.IsSessionRevoked)
        {
            return Result.Failure(Error.Unauthorized("SESSION_REVOKED", "User session has been revoked."));
        }

        if (user.MfaRequired && !user.MfaCompleted)
        {
            return Result.Failure(Error.Forbidden("MFA_REQUIRED", "Multi-factor authentication required."));
        }

        return Result.Success();
    }

    private static Result EvaluateTier2(TenantIdentity tenant, ResourceRequirement requirement)
    {
        if (!tenant.TenantId.HasValue || tenant.TenantId.Value == Guid.Empty || (!tenant.IsMember && !tenant.IsSupportAccess))
        {
            return Result.Failure(Error.Forbidden("TENANT_MEMBERSHIP_REQUIRED", "User is not an active member of this tenant."));
        }

        if (string.IsNullOrWhiteSpace(tenant.SubscriptionStatus))
        {
            return Result.Failure(Error.Forbidden("TENANT_STATUS_INVALID", "Tenant subscription status is missing or invalid."));
        }

        if (TerminatedStatuses.Contains(tenant.SubscriptionStatus))
        {
            return Result.Failure(Error.Forbidden("TENANT_SUSPENDED", "Tenant subscription is suspended."));
        }

        if (string.Equals(tenant.SubscriptionStatus, "PastDue", StringComparison.OrdinalIgnoreCase))
        {
            if (!requirement.IsReadOperation && !requirement.AllowPastDue)
            {
                return Result.Failure(Error.Forbidden("TENANT_READ_ONLY_PAST_DUE", "Tenant account is past due. Mutating operations are locked in read-only mode."));
            }
        }
        else if (!ActiveStatuses.Contains(tenant.SubscriptionStatus))
        {
            return Result.Failure(Error.Forbidden("TENANT_STATUS_UNRECOGNIZED", $"Tenant subscription status '{tenant.SubscriptionStatus}' is unrecognized."));
        }

        if (tenant.IsSupportAccess && !requirement.IsReadOperation)
        {
            return Result.Failure(Error.Forbidden("SUPPORT_ACCESS_READ_ONLY", "Support access sessions cannot perform mutating operations."));
        }

        return Result.Success();
    }

    private static Result EvaluateTier3(TenantIdentity tenant, ResourceRequirement requirement)
    {
        if (!string.IsNullOrWhiteSpace(requirement.RequiredModule))
        {
            if (tenant.ActiveModules == null || !tenant.ActiveModules.Contains(requirement.RequiredModule))
            {
                return Result.Failure(Error.Forbidden("PLAN_MODULE_NOT_ENTITLED", $"Tenant plan does not include module '{requirement.RequiredModule}'."));
            }
        }

        if (requirement.MaxEmployeeQuota.HasValue || requirement.CurrentEmployeeCount.HasValue)
        {
            if (requirement.MaxEmployeeQuota < 0 || requirement.CurrentEmployeeCount < 0)
            {
                return Result.Failure(Error.Forbidden("PLAN_QUOTA_INVALID", "Tenant employee quota or count cannot be negative."));
            }

            if (requirement.MaxEmployeeQuota.HasValue && !requirement.CurrentEmployeeCount.HasValue)
            {
                return Result.Failure(Error.Forbidden("PLAN_QUOTA_INVALID", "Current employee count is required when evaluating plan quota."));
            }

            if (requirement.MaxEmployeeQuota.HasValue &&
                requirement.CurrentEmployeeCount.HasValue &&
                requirement.CurrentEmployeeCount.Value >= requirement.MaxEmployeeQuota.Value)
            {
                return Result.Failure(Error.Forbidden("PLAN_QUOTA_EXCEEDED", "Tenant employee quota has been reached."));
            }
        }

        return Result.Success();
    }

    private static Result EvaluateTier4(AuthIdentity user, ResourceRequirement requirement, EvaluationSubject subject)
    {
        if (subject.IsReadOnlyRole && !requirement.IsReadOperation)
        {
            return Result.Failure(Error.Forbidden("READ_ONLY_ROLE_MUTATION_DENIED", "Read-only role cannot perform mutating operations."));
        }

        if (!string.IsNullOrWhiteSpace(requirement.RequiredPermission))
        {
            if (subject.UserPermissions == null || !subject.UserPermissions.Contains(requirement.RequiredPermission))
            {
                return Result.Failure(Error.Forbidden("PERMISSION_DENIED", $"Missing required permission '{requirement.RequiredPermission}'."));
            }
        }

        if (subject.ProhibitSelfApproval && user.UserId.HasValue && subject.TargetOwnerId.HasValue)
        {
            if (user.UserId.Value == subject.TargetOwnerId.Value)
            {
                return Result.Failure(Error.Forbidden("SELF_APPROVAL_PROHIBITED", "Approver cannot approve their own submission."));
            }
        }

        if (subject.GrantedScope.HasValue)
        {
            if (subject.GrantedScope == DataScope.Self)
            {
                if (!user.UserId.HasValue || user.UserId.Value == Guid.Empty ||
                    !subject.TargetOwnerId.HasValue || subject.TargetOwnerId.Value == Guid.Empty ||
                    subject.TargetOwnerId.Value != user.UserId.Value)
                {
                    return Result.Failure(Error.Forbidden("SCOPE_ACCESS_DENIED", "Access denied: Self scope only permits accessing own records."));
                }
            }

            if (!subject.IsTargetWithinScope)
            {
                return Result.Failure(Error.Forbidden("SCOPE_ACCESS_DENIED", "Access denied: Target entity is outside authorized data scope."));
            }

            if (subject.ActorScopeUnitId.HasValue || subject.TargetScopeUnitId.HasValue)
            {
                if (!subject.ActorScopeUnitId.HasValue || !subject.TargetScopeUnitId.HasValue ||
                    subject.ActorScopeUnitId.Value != subject.TargetScopeUnitId.Value)
                {
                    return Result.Failure(Error.Forbidden("SCOPE_ACCESS_DENIED", "Access denied: Target entity is outside actor organizational unit scope."));
                }
            }
        }

        if (subject.ConflictingRoles != null && subject.UserRoles != null)
        {
            var conflictCount = 0;
            foreach (var role in subject.UserRoles)
            {
                if (subject.ConflictingRoles.Contains(role))
                {
                    conflictCount++;
                    if (conflictCount > 1)
                    {
                        return Result.Failure(Error.Forbidden("SOD_VIOLATION", "Separation of duties conflict detected."));
                    }
                }
            }
        }

        return Result.Success();
    }
}
