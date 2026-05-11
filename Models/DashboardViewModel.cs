namespace HRSystem.Models;

public class DashboardViewModel
{
    public bool IsAdmin { get; set; }

    public AttendanceSummaryViewModel Attendance { get; set; } = new();

    public AdminDashboardSummaryViewModel AdminSummary { get; set; } = new();

    public SupervisorDashboardSummaryViewModel SupervisorSummary { get; set; } = new();

    public IReadOnlyList<TaskItem> ActiveTasks { get; set; } = [];

    public IReadOnlyList<Notification> Notifications { get; set; } = [];

    public IReadOnlyList<Rotation> Rotations { get; set; } = [];

    public IReadOnlyList<ManagedUserSummaryViewModel> ManagedUsers { get; set; } = [];

    public IReadOnlyList<AttendanceReportViewModel> AttendanceReports { get; set; } = [];

    public PagedResultViewModel<ManagedUserSummaryViewModel> ManagedUsersPage { get; set; } = new();

    public PagedResultViewModel<AttendanceReportViewModel> AttendanceReportsPage { get; set; } = new();
}

public class AttendanceSummaryViewModel
{
    /// <summary>Calendar days of approved leave overlapping the current month (current user).</summary>
    public int ApprovedLeaveDaysThisMonth { get; set; }

    /// <summary>Calendar days of approved leave from Jan 1 through today (current user).</summary>
    public int ApprovedLeaveDaysYearToDate { get; set; }

    public int PendingLeaveRequests { get; set; }

    public DateOnly? NextApprovedLeaveStart { get; set; }
}

public class AdminDashboardSummaryViewModel
{
    public int TotalUsers { get; set; }

    public int AdminUsers { get; set; }

    public int SupervisorUsers { get; set; }

    public int EmployeeUsers { get; set; }

    public int PendingLeaveRequests { get; set; }

    public int OpenTasks { get; set; }

    public int ActiveRotations { get; set; }

    public int UnreadNotifications { get; set; }
}

public class SupervisorDashboardSummaryViewModel
{
    public int PendingLeaveRequests { get; set; }

    public int OpenTasks { get; set; }

    public int ActiveRotations { get; set; }

    public int ManagedUsers { get; set; }

    public int AttendanceReports { get; set; }
}

public class ManagedUserSummaryViewModel
{
    public string UserId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;
}

public class AttendanceReportViewModel
{
    public string UserId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public int ApprovedLeaveDaysThisMonth { get; set; }

    public int PendingLeaveRequests { get; set; }
}
