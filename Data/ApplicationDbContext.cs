using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using HRSystem.Models;

namespace HRSystem.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<IdentityUser>(options)
{
    public DbSet<EmployeeProfile> EmployeeProfiles => Set<EmployeeProfile>();

    public DbSet<Rotation> Rotations => Set<Rotation>();

    public DbSet<RotationScheduleEntry> RotationScheduleEntries => Set<RotationScheduleEntry>();

    public DbSet<LeaveRequest> LeaveRequests => Set<LeaveRequest>();

    public DbSet<TaskItem> TaskItems => Set<TaskItem>();

    public DbSet<DailyTaskTemplate> DailyTaskTemplates => Set<DailyTaskTemplate>();

    public DbSet<Notification> Notifications => Set<Notification>();

    public DbSet<ChangeLogEntry> ChangeLogEntries => Set<ChangeLogEntry>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<EmployeeProfile>(entity =>
        {
            entity.HasIndex(x => x.UserId).IsUnique();
            entity.HasIndex(x => x.EmployeeCode).IsUnique();
            entity.HasOne(x => x.User)
                .WithOne()
                .HasForeignKey<EmployeeProfile>(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ChangeLogEntry>(entity =>
        {
            entity.HasIndex(x => x.CreatedAtUtc);
            entity.HasIndex(x => x.EntityType);
            entity.HasIndex(x => x.Action);
        });

        builder.Entity<DailyTaskTemplate>(entity =>
        {
            entity.HasIndex(x => x.Title).IsUnique();
            entity.HasIndex(x => x.SortOrder);
        });
    }
}
