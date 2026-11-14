using Microsoft.EntityFrameworkCore;
using PeopleFlow.Kernel.Time;
using PeopleFlow.Modules.Tenancy.Contracts;
using PeopleFlow.Modules.Tenancy.Core.Entities;
using PeopleFlow.Modules.Tenancy.Core.Persistence;

namespace PeopleFlow.Modules.Tenancy.Core.Services;

public sealed class TenantRoleSeeder
{
    private sealed record RoleDefinition(
        string Code,
        string Name,
        string Description,
        bool IsReadOnlyRole,
        IReadOnlyList<string> Permissions);

    private static readonly IReadOnlyList<RoleDefinition> BuiltinRoles =
    [
        new(
            RoleCodes.CompanyOwner,
            "Company Owner",
            "Full company owner access across subscribed modules and tenant governance.",
            false,
            Permissions.AllCatalog),
        new(
            RoleCodes.HrAdmin,
            "HR Admin",
            "Manages employees, organization structure, time, leave, performance, and user roles.",
            false,
            [
                Permissions.OrgStructureRead, Permissions.OrgStructureManage,
                Permissions.EmployeeRead, Permissions.EmployeeWrite, Permissions.EmployeeBankAccountChangeRequest,
                Permissions.TimeAttendanceRead, Permissions.TimeAttendanceWrite, Permissions.TimeAttendanceApprove, Permissions.TimeShiftManage,
                Permissions.LeaveRequestCreate, Permissions.LeaveRequestRead, Permissions.LeaveRequestApprove,
                Permissions.ExpenseClaimSubmit, Permissions.ExpenseClaimApprove,
                Permissions.PerformanceReviewRead, Permissions.PerformanceReviewManage,
                Permissions.RecruitmentPipelineManage, Permissions.LearningCourseAssign, Permissions.AnalyticsReportRead,
                Permissions.SettingsRolesRead, Permissions.SettingsRolesManage,
                Permissions.SettingsMembersRead, Permissions.SettingsMembersManage,
                Permissions.AuditLogsRead
            ]),
        new(
            RoleCodes.PayrollOfficer,
            "Payroll Officer",
            "Calculates payroll, approves bank account changes, and accesses payroll reporting.",
            false,
            [
                Permissions.OrgStructureRead,
                Permissions.EmployeeRead, Permissions.EmployeeBankAccountApprove,
                Permissions.TimeAttendanceRead,
                Permissions.LeaveRequestRead,
                Permissions.PayrollRunRead, Permissions.PayrollRunCalculate, Permissions.PayrollRunApprove, Permissions.PayrollPayslipReadOwn,
                Permissions.ExpenseClaimApprove,
                Permissions.AnalyticsReportRead
            ]),
        new(
            RoleCodes.Manager,
            "Manager",
            "Reviews and approves attendance, leave, expenses, and performance for their scoped team.",
            false,
            [
                Permissions.OrgStructureRead,
                Permissions.EmployeeRead,
                Permissions.TimeAttendanceRead, Permissions.TimeAttendanceApprove,
                Permissions.LeaveRequestRead, Permissions.LeaveRequestApprove,
                Permissions.ExpenseClaimApprove,
                Permissions.PerformanceReviewRead, Permissions.PerformanceReviewManage
            ]),
        new(
            RoleCodes.Employee,
            "Employee",
            "Self-service access for personal profile, attendance, leave requests, expenses, and payslips.",
            false,
            [
                Permissions.OrgStructureRead,
                Permissions.EmployeeRead,
                Permissions.TimeAttendanceRead, Permissions.TimeAttendanceWrite,
                Permissions.LeaveRequestCreate, Permissions.LeaveRequestRead,
                Permissions.PayrollPayslipReadOwn,
                Permissions.ExpenseClaimSubmit,
                Permissions.PerformanceReviewRead
            ]),
        new(
            RoleCodes.Auditor,
            "Auditor",
            "Strictly read-only compliance and audit inspection role with optional time-bound access.",
            true,
            [
                Permissions.OrgStructureRead,
                Permissions.EmployeeRead,
                Permissions.TimeAttendanceRead,
                Permissions.LeaveRequestRead,
                Permissions.PayrollRunRead,
                Permissions.PerformanceReviewRead,
                Permissions.AnalyticsReportRead,
                Permissions.SettingsRolesRead,
                Permissions.SettingsMembersRead,
                Permissions.SettingsSubscriptionRead,
                Permissions.AuditLogsRead
            ])
    ];

    private readonly TenancyDbContext _db;
    private readonly IClock _clock;

    public TenantRoleSeeder(TenancyDbContext db, IClock clock)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task SeedDefaultRolesAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        using var _ = _db.BeginOperationTenantScope(tenantId);

        var existingCodes = await _db.TenancyRoles
            .Where(r => r.TenantId == tenantId)
            .Select(r => r.Code)
            .ToListAsync(cancellationToken);

        var existingSet = new HashSet<string>(existingCodes, StringComparer.OrdinalIgnoreCase);

        foreach (var def in BuiltinRoles)
        {
            if (existingSet.Contains(def.Code))
            {
                continue;
            }

            var roleId = Guid.CreateVersion7();
            _db.TenancyRoles.Add(new TenancyRole(roleId)
            {
                TenantId = tenantId,
                Code = def.Code,
                Name = def.Name,
                Description = def.Description,
                IsSystemBuiltin = true,
                IsReadOnlyRole = def.IsReadOnlyRole
            });

            foreach (var permission in def.Permissions.Distinct(StringComparer.Ordinal))
            {
                _db.TenancyRolePermissions.Add(new TenancyRolePermission
                {
                    TenantId = tenantId,
                    RoleId = roleId,
                    PermissionCode = permission
                });
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
    }
}
