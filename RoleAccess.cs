public static class RoleAccess
{
    public static bool IsFullAccessRole(string? role) => role is "ADMIN" or "MANAGER";
    public static bool HasFullAccess(IEnumerable<string> roles) => roles.Any(IsFullAccessRole);
    // Keep the person's real role label while granting the same capabilities.
    public static string WorkspaceRole(IEnumerable<string> roles) => roles.Contains("ADMIN", StringComparer.Ordinal) ? "ADMIN" : "MANAGER";
}
