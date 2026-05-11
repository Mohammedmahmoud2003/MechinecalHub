namespace HRSystem.Models;

public enum LeaveRequestStatus
{
    Draft = 0,
    Pending = 1,
    Approved = 2,
    Rejected = 3,
    Cancelled = 4
}

public class LeaveRequest
{
    public int Id { get; set; }

    public string EmployeeUserId { get; set; } = string.Empty;

    public DateOnly StartDate { get; set; }

    public DateOnly EndDate { get; set; }

    public string? Reason { get; set; }

    public LeaveRequestStatus Status { get; set; }

    public DateTimeOffset SubmittedAtUtc { get; set; }

    public DateTimeOffset? ReviewedAtUtc { get; set; }

    public string? ReviewerUserId { get; set; }
}
