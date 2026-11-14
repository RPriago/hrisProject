namespace PeopleFlow.Modules.Tenancy.Contracts;

public static class RoleCodes
{
    public const string CompanyOwner = "CompanyOwner";
    public const string HrAdmin = "HrAdmin";
    public const string PayrollOfficer = "PayrollOfficer";
    public const string Manager = "Manager";
    public const string Employee = "Employee";
    public const string Auditor = "Auditor";
}

public static class IndustryPresets
{
    public const string OfficeServices = "OfficeServices";
    public const string RetailRestaurant = "RetailRestaurant";
    public const string ManufacturingField = "ManufacturingField";
    public const string ConsultingAgency = "ConsultingAgency";
    public const string RemoteHybrid = "RemoteHybrid";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        OfficeServices,
        RetailRestaurant,
        ManufacturingField,
        ConsultingAgency,
        RemoteHybrid
    };
}

public static class Permissions
{
    public const string OrgStructureRead = "org.structure.read";
    public const string OrgStructureManage = "org.structure.manage";

    public const string EmployeeRead = "employee.profile.read";
    public const string EmployeeWrite = "employee.profile.write";
    public const string EmployeeBankAccountChangeRequest = "employee.bank_account.request";
    public const string EmployeeBankAccountApprove = "employee.bank_account.approve";

    public const string TimeAttendanceRead = "time.attendance.read";
    public const string TimeAttendanceWrite = "time.attendance.write";
    public const string TimeAttendanceApprove = "time.attendance.approve";
    public const string TimeShiftManage = "time.shift.manage";

    public const string LeaveRequestCreate = "leave.request.create";
    public const string LeaveRequestRead = "leave.request.read";
    public const string LeaveRequestApprove = "leave.request.approve";

    public const string PayrollRunRead = "payroll.run.read";
    public const string PayrollRunCalculate = "payroll.run.calculate";
    public const string PayrollRunApprove = "payroll.run.approve";
    public const string PayrollPayslipReadOwn = "payroll.payslip.read_own";

    public const string ExpenseClaimSubmit = "expense.claim.submit";
    public const string ExpenseClaimApprove = "expense.claim.approve";

    public const string PerformanceReviewRead = "performance.review.read";
    public const string PerformanceReviewManage = "performance.review.manage";

    public const string RecruitmentPipelineManage = "recruitment.pipeline.manage";
    public const string LearningCourseAssign = "learning.course.assign";
    public const string AnalyticsReportRead = "analytics.report.read";

    public const string SettingsRolesRead = "settings.roles.read";
    public const string SettingsRolesManage = "settings.roles.manage";
    public const string SettingsMembersRead = "settings.members.read";
    public const string SettingsMembersManage = "settings.members.manage";
    public const string SettingsIntegrationsManage = "settings.integrations.manage";
    public const string SettingsSubscriptionRead = "settings.subscription.read";
    public const string SettingsSubscriptionManage = "settings.subscription.manage";
    public const string SettingsSupportGrantsManage = "settings.support_grants.manage";
    public const string AuditLogsRead = "audit.logs.read";

    public static readonly IReadOnlySet<string> FinancialReadPermissions = new HashSet<string>(StringComparer.Ordinal)
    {
        PayrollRunRead,
        PayrollPayslipReadOwn,
        AnalyticsReportRead,
        SettingsSubscriptionRead
    };

    public static readonly IReadOnlySet<string> ReadOnlyCatalog = new HashSet<string>(StringComparer.Ordinal)
    {
        OrgStructureRead,
        EmployeeRead,
        TimeAttendanceRead,
        LeaveRequestRead,
        PayrollRunRead,
        PayrollPayslipReadOwn,
        PerformanceReviewRead,
        AnalyticsReportRead,
        SettingsRolesRead,
        SettingsMembersRead,
        SettingsSubscriptionRead,
        AuditLogsRead
    };

    public static bool IsReadOnlyPermission(string permissionCode) =>
        !string.IsNullOrWhiteSpace(permissionCode) && ReadOnlyCatalog.Contains(permissionCode);

    public static readonly IReadOnlyList<string> AllCatalog = new[]
    {
        OrgStructureRead, OrgStructureManage,
        EmployeeRead, EmployeeWrite, EmployeeBankAccountChangeRequest, EmployeeBankAccountApprove,
        TimeAttendanceRead, TimeAttendanceWrite, TimeAttendanceApprove, TimeShiftManage,
        LeaveRequestCreate, LeaveRequestRead, LeaveRequestApprove,
        PayrollRunRead, PayrollRunCalculate, PayrollRunApprove, PayrollPayslipReadOwn,
        ExpenseClaimSubmit, ExpenseClaimApprove,
        PerformanceReviewRead, PerformanceReviewManage,
        RecruitmentPipelineManage, LearningCourseAssign, AnalyticsReportRead,
        SettingsRolesRead, SettingsRolesManage,
        SettingsMembersRead, SettingsMembersManage,
        SettingsIntegrationsManage, SettingsSubscriptionRead, SettingsSubscriptionManage,
        SettingsSupportGrantsManage, AuditLogsRead
    };

    public static string ResolveRequiredModule(string permissionCode)
    {
        if (string.IsNullOrWhiteSpace(permissionCode)) return "CoreHR";
        var prefix = permissionCode.Split('.', 2)[0].ToLowerInvariant();
        return prefix switch
        {
            "payroll" => "Payroll",
            "time" or "attendance" => "Time",
            "leave" => "Leave",
            "expense" => "Expense",
            "performance" => "Performance",
            "recruitment" => "Recruitment",
            "learning" => "Learning",
            "analytics" => "Analytics",
            _ => "CoreHR"
        };
    }
}
