namespace HRSystem.Models;

public sealed class AdminUsersViewModel
{
    public PagedResultViewModel<AdminUserRowViewModel> Users { get; init; } = new();

    public List<RoleOptionViewModel> RoleOptions { get; init; } = [];

    public string? StatusMessage { get; init; }
}
