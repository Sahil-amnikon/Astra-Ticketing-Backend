using Astra.Api.Infrastructure;
using Astra.Api.Modules;
using Astra.Api.Security;

namespace Astra.Tests;

public class ProblemWorkflowTests
{
    private static Actor Actor(Guid id, string role) => new(
        id, "Support User", "support@example.test", new HashSet<string>([role], StringComparer.Ordinal),
        new HashSet<string>(StringComparer.Ordinal), [], [], "operations", "NONE", "ASSIGNED");

    private static Dictionary<string, object?> Current(Guid owner, string status = "OPEN", int revision = 1) => new(StringComparer.Ordinal)
    {
        ["owner_user_id"] = owner,
        ["status"] = status,
        ["revision"] = revision
    };

    private static UpdateProblemInput Input(Guid owner, string status = "INVESTIGATING", int revision = 1) => new(
        revision, "Network outage", "Investigating packet loss", "P2_HIGH", status, "", "", "", owner);

    [Fact]
    public void OwnerCanMakeAnAllowedInvestigationUpdate()
    {
        var owner = Guid.NewGuid();
        Problems.ValidateUpdate(Input(owner), Current(owner), Actor(owner, "TECHNICIAN"));
    }

    [Fact]
    public void NonOwnerCannotEditAnotherUsersProblem()
    {
        var owner = Guid.NewGuid();
        var error = Assert.Throws<ApiFault>(() => Problems.ValidateUpdate(Input(owner), Current(owner), Actor(Guid.NewGuid(), "TECHNICIAN")));
        Assert.Equal(403, error.Status);
    }

    [Fact]
    public void StaleRevisionAndInvalidStatusTransitionAreRejected()
    {
        var owner = Guid.NewGuid();
        Assert.Equal(409, Assert.Throws<ApiFault>(() => Problems.ValidateUpdate(Input(owner, revision: 2), Current(owner), Actor(owner, "TECHNICIAN"))).Status);
        Assert.Equal(400, Assert.Throws<ApiFault>(() => Problems.ValidateUpdate(Input(owner, status: "CLOSED"), Current(owner), Actor(owner, "MANAGER"))).Status);
    }

    [Fact]
    public void ResolutionRequiresRootCauseAndResolutionDetails()
    {
        var owner = Guid.NewGuid();
        Assert.Equal(400, Assert.Throws<ApiFault>(() => Problems.ValidateUpdate(Input(owner, status: "RESOLVED"), Current(owner), Actor(owner, "TECHNICIAN"))).Status);
        var valid = Input(owner, status: "RESOLVED") with { RootCause = "Failed switch", Resolution = "Replaced switch" };
        Problems.ValidateUpdate(valid, Current(owner), Actor(owner, "TECHNICIAN"));
    }

    [Fact]
    public void ManagerCanCloseOnlyAfterRootCauseAndResolutionAreRecorded()
    {
        var manager = Guid.NewGuid();
        var close = Input(manager, status: "CLOSED") with { RootCause = "Failed switch", Resolution = "Replaced switch" };
        Problems.ValidateUpdate(close, Current(Guid.NewGuid(), "RESOLVED"), Actor(manager, "MANAGER"));
        var incomplete = close with { Resolution = "" };
        Assert.Equal(400, Assert.Throws<ApiFault>(() => Problems.ValidateUpdate(incomplete, Current(Guid.NewGuid(), "RESOLVED"), Actor(manager, "MANAGER"))).Status);
    }
}
