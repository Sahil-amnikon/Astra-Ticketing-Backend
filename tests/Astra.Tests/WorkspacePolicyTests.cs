using Astra.Api.Security;

namespace Astra.Tests;

public class WorkspacePolicyTests
{
    private static Actor Admin() => new(Guid.NewGuid(), "Administrator", "admin@example.invalid", new HashSet<string> { "ADMIN" }, new HashSet<string> { "*" }, [], [], "admin", "CHANGE_MANAGER", "ALL");

    [Fact]
    public void SupportWorkspaceDoesNotInheritAdministrativeRoles()
    {
        var account = Admin();
        var technician = WorkspacePolicy.Select(account, "operations");
        Assert.Contains("TECHNICIAN", technician.Roles);
        Assert.DoesNotContain("ADMIN", technician.Roles);
        Assert.DoesNotContain("*", technician.Permissions);
        Assert.Contains("TICKETS_ASSIGN", technician.Permissions);
        Assert.Equal("CONTRIBUTOR", technician.ChangeAccess);
        Assert.Contains("ADMIN", account.Roles);
        Assert.Throws<ApiFault>(() => WorkspacePolicy.Select(technician, "admin"));
    }

    [Fact]
    public void StaffGroupsDoNotGrantAdministrativeAccessOrElevateInterns()
    {
        var grants = new HashSet<string> { "REPORTS_VIEW", "USERS_MANAGE", "*" };
        var technician = WorkspacePolicy.Select(Admin() with { GroupPermissions = grants }, "operations");
        Assert.Contains("REPORTS_VIEW", technician.Permissions);
        Assert.DoesNotContain("USERS_MANAGE", technician.Permissions);
        Assert.DoesNotContain("*", technician.Permissions);
        var intern = WorkspacePolicy.Select(Admin() with { Roles = new HashSet<string> { "INTERN" }, GroupPermissions = grants }, "operations");
        Assert.Empty(intern.Permissions);
    }

    [Theory]
    [InlineData("POST", "/tickets")]
    [InlineData("POST", "/tickets/record/comments")]
    [InlineData("PATCH", "/changes/record/status")]
    [InlineData("POST", "/problems")]
    [InlineData("DELETE", "/problems/record/tickets/ticket")]
    [InlineData("POST", "/internal/tickets/record/help")]
    [InlineData("POST", "/change-governance/record/cab")]
    [InlineData("POST", "/mail/intake/message")]
    public void AdministrationCannotMutateServiceRecords(string method, string path)
    {
        Assert.Throws<ApiFault>(() => WorkspacePolicy.RequireOperational(Admin(), method, path));
        WorkspacePolicy.RequireOperational(WorkspacePolicy.Select(Admin(), "operations"), method, path);
    }

    [Theory]
    [InlineData("GET", "/tickets")]
    [InlineData("PATCH", "/change-governance/policy")]
    [InlineData("POST", "/change-governance/templates")]
    [InlineData("PATCH", "/admin/users/user")]
    [InlineData("POST", "/tickets/views")]
    public void AdministrationCanReadAndConfigure(string method, string path) => WorkspacePolicy.RequireOperational(Admin(), method, path);
}
