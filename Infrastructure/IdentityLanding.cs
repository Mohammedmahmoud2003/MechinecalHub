using System.Security.Claims;

namespace HRSystem.Infrastructure;

public static class IdentityLanding
{
    public static string GetPath(ClaimsPrincipal user)
    {
        if (user.IsInRole(AppRoles.Admin))
        {
            return GetPath(AppRoles.Admin);
        }

        if (user.IsInRole(AppRoles.Manager))
        {
            return GetPath(AppRoles.Manager);
        }

        return GetPath(AppRoles.Employee);
    }

    public static string GetPath(IEnumerable<string> roles)
    {
        var roleSet = roles.ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (roleSet.Contains(AppRoles.Admin))
        {
            return "/Dashboard";
        }

        if (roleSet.Contains(AppRoles.Manager))
        {
            return "/Dashboard";
        }

        return "/Dashboard";
    }

    public static string GetPath(string role) =>
        string.Equals(role, AppRoles.Manager, StringComparison.OrdinalIgnoreCase)
            ? "/Dashboard"
            : "/Dashboard";
}
