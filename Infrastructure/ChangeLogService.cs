using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using HRSystem.Data;
using HRSystem.Hubs;
using HRSystem.Models;

namespace HRSystem.Infrastructure;

public sealed class ChangeLogService(
    ApplicationDbContext context,
    IHubContext<ChangeLogHub> hubContext)
{
    private readonly ApplicationDbContext _context = context;
    private readonly IHubContext<ChangeLogHub> _hubContext = hubContext;

    public async Task<ChangeLogRowViewModel> LogAsync(
        ApplicationDbContext _,
        string entityType,
        string action,
        string summary,
        string? details = null,
        string? actorUserId = null)
    {
        var entry = new ChangeLogEntry
        {
            ActorUserId = actorUserId,
            EntityType = entityType,
            Action = action,
            Summary = summary,
            Details = details,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        _context.ChangeLogEntries.Add(entry);
        await _context.SaveChangesAsync();

        var row = await BuildRowAsync(entry);
        await _hubContext.Clients.All.SendAsync("ChangeLogged", row);
        return row;
    }

    private async Task<ChangeLogRowViewModel> BuildRowAsync(ChangeLogEntry entry)
    {
        string actorName = "System";
        if (!string.IsNullOrWhiteSpace(entry.ActorUserId))
        {
            var actor = await _context.Users
                .Where(u => u.Id == entry.ActorUserId)
                .Select(u => new { u.Id, u.Email, u.UserName })
                .FirstOrDefaultAsync();

            if (actor is not null)
            {
                var profile = await _context.EmployeeProfiles.AsNoTracking()
                    .FirstOrDefaultAsync(p => p.UserId == actor.Id);
                actorName = EmployeeDirectoryService.GetDisplayName(
                    new IdentityUser { Id = actor.Id, Email = actor.Email, UserName = actor.UserName },
                    profile);
            }
        }

        return new ChangeLogRowViewModel
        {
            Id = entry.Id,
            ActorName = actorName,
            EntityType = entry.EntityType,
            Action = entry.Action,
            Summary = entry.Summary,
            Details = entry.Details,
            CreatedAtUtc = entry.CreatedAtUtc
        };
    }
}
