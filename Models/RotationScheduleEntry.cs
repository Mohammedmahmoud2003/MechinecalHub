namespace HRSystem.Models;

public class RotationScheduleEntry
{
    public int Id { get; set; }

    public int RotationId { get; set; }

    public Rotation? Rotation { get; set; }

    public string EmployeeUserId { get; set; } = string.Empty;

    public string EmployeeCode { get; set; } = string.Empty;

    public string EmployeeName { get; set; } = string.Empty;

    public string JobTitle { get; set; } = string.Empty;

    public DateOnly WorkDate { get; set; }

    public string OriginalStatus { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;
}
