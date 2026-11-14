using Microsoft.EntityFrameworkCore;
using PeopleFlow.Kernel.Common;
using PeopleFlow.Modules.Tenancy.Contracts;
using PeopleFlow.Modules.Tenancy.Core.Persistence;

namespace PeopleFlow.Modules.Tenancy.Core.Services;

public sealed class SodPolicyEvaluator : ISodPolicyEvaluator
{
    private readonly TenancyDbContext _db;

    public SodPolicyEvaluator(TenancyDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task<Result> EvaluateAsync(SodEvaluationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.TenantId == Guid.Empty ||
            request.ActorUserId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.OperationType))
        {
            return Result.Failure(Error.Forbidden("SOD_INVALID_REQUEST", "Tenant ID, actor user ID, and operation type are required for SoD evaluation."));
        }

        if (request.TargetOwnerUserId.HasValue && request.TargetOwnerUserId.Value == request.ActorUserId)
        {
            return Result.Failure(new Error(
                "SELF_APPROVAL_PROHIBITED",
                $"User cannot self-approve operation '{request.OperationType}'."));
        }

        var tenant = await _db.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == request.TenantId, cancellationToken);
        if (tenant is null)
        {
            return Result.Failure(Error.Forbidden("SOD_TENANT_NOT_FOUND", "Tenant was not found for SoD evaluation."));
        }

        var operationType = request.OperationType.Trim();
        var isPayrollOrBankSod =
            operationType.Equals("PayrollRunApprove", StringComparison.OrdinalIgnoreCase) ||
            operationType.Equals(Permissions.PayrollRunApprove, StringComparison.OrdinalIgnoreCase) ||
            operationType.Equals("EmployeeBankAccountApprove", StringComparison.OrdinalIgnoreCase) ||
            operationType.Equals(Permissions.EmployeeBankAccountApprove, StringComparison.OrdinalIgnoreCase);

        if (isPayrollOrBankSod && tenant.EnforcePayrollSod)
        {
            if (!request.InitiatedOrCreatedByUserId.HasValue || request.InitiatedOrCreatedByUserId.Value == Guid.Empty)
            {
                return Result.Failure(Error.Forbidden(
                    "SOD_INITIATOR_REQUIRED",
                    $"Initiator user ID is required to verify segregation of duties for '{operationType}'."));
            }

            if (request.InitiatedOrCreatedByUserId.Value == request.ActorUserId)
            {
                return Result.Failure(new Error(
                    "SOD_VIOLATION",
                    $"Segregation of duties prohibits the creator from approving '{operationType}'."));
            }
        }

        return Result.Success();
    }
}
