namespace HRSystem.Models;

public enum NotificationStatus
{
    Unread = 0,
    Read = 1,
    Archived = 2
}

public class Notification
{
    public int Id { get; set; }

    public string RecipientUserId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    public NotificationStatus Status { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? ReadAtUtc { get; set; }
}
