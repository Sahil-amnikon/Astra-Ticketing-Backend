using System.Text.Json;
using MySqlConnector;

public sealed record ChangeRequestSelection(string[] Sites, string[] Departments, string RequesterKind, string RequesterId, string Approver);
public sealed record ChangeReviewPermission(bool CanReviewChanges,bool CanReviewTemplates);
public sealed partial class MySqlStore
{
    private static bool ChangeApprovalRule(string type,string actor,string creator,string owner,string? requester,string selected) =>
        actor==selected && (type=="STANDARD" || type=="NORMAL" && actor!=creator && actor!=owner && actor!=requester);
    private static string[] ChangeSelectionIds(JsonElement body,string plural,string singular,int limit)
    {
        if(body.TryGetProperty(plural,out var list)&&list.ValueKind!=JsonValueKind.Array)throw new ArgumentException($"Choose a valid list of {plural}.");
        var ids=body.TryGetProperty(plural,out _)?JArray(body,plural).Select(value=>value.ValueKind==JsonValueKind.String?FGuid(value.GetString(),plural):throw new ArgumentException($"Choose valid {plural}.")).Distinct().ToArray():JOptional(body,singular) is string id?[FGuid(id,singular)]:[];
        if(ids.Length>limit)throw new ArgumentException($"Choose at most {limit} {plural}.");return ids;
    }
    private static string ChangePersonScopeSql(string user,string client) => $"EXISTS(SELECT 1 FROM users person WHERE person.id={user} AND person.active=1 AND (person.access_expires_at IS NULL OR person.access_expires_at>UTC_TIMESTAMP(6)) AND (EXISTS(SELECT 1 FROM client_users membership WHERE membership.user_id=person.id AND membership.client_id={client}) OR EXISTS(SELECT 1 FROM contacts contact WHERE contact.user_id=person.id AND contact.client_id={client} AND contact.active=1) OR EXISTS(SELECT 1 FROM user_roles assigned_role WHERE assigned_role.user_id=person.id AND assigned_role.role_code IN ('ADMIN','MANAGER'))))";
    private static string ChangePersonSiteScopeSql(string user,string sites) => $"(EXISTS(SELECT 1 FROM user_roles global_role WHERE global_role.user_id={user} AND global_role.role_code IN ('ADMIN','MANAGER')) OR (EXISTS(SELECT 1 FROM user_sites restriction WHERE restriction.user_id={user}) AND JSON_LENGTH({sites})>0 AND (SELECT COUNT(DISTINCT site.id) FROM sites site JOIN user_sites allowed ON allowed.site_id=site.id AND allowed.user_id={user} WHERE site.active=1 AND JSON_CONTAINS({sites},JSON_QUOTE(site.id)))=JSON_LENGTH({sites})) OR (NOT EXISTS(SELECT 1 FROM user_sites restriction WHERE restriction.user_id={user}) AND (EXISTS(SELECT 1 FROM user_roles support_role WHERE support_role.user_id={user} AND support_role.role_code IN ('INTERN','TECHNICIAN','TEAM_LEAD')) OR NOT EXISTS(SELECT 1 FROM contacts boundary WHERE boundary.user_id={user} AND boundary.site_id IS NOT NULL) OR (JSON_LENGTH({sites})>0 AND (SELECT COUNT(DISTINCT site.id) FROM sites site JOIN contacts boundary ON boundary.site_id=site.id AND boundary.user_id={user} AND boundary.active=1 WHERE site.active=1 AND JSON_CONTAINS({sites},JSON_QUOTE(site.id)))=JSON_LENGTH({sites})))))";
    private static string ChangeOrganizationObserverSql(string user,string client) => $"EXISTS(SELECT 1 FROM users observer WHERE observer.id={user} AND observer.active=1 AND (observer.access_expires_at IS NULL OR observer.access_expires_at>UTC_TIMESTAMP(6)) AND (EXISTS(SELECT 1 FROM user_roles assigned_role WHERE assigned_role.user_id=observer.id AND assigned_role.role_code IN ('ADMIN','MANAGER')) OR (EXISTS(SELECT 1 FROM user_roles assigned_role WHERE assigned_role.user_id=observer.id AND assigned_role.role_code='TEAM_LEAD') AND EXISTS(SELECT 1 FROM client_users membership WHERE membership.user_id=observer.id AND membership.client_id={client}))))";
    private static string RequestedChangeUserSql(string detail) => $"CASE WHEN {detail}.requester_kind='USER' THEN {detail}.requester_id ELSE (SELECT contact.user_id FROM contacts contact WHERE contact.id={detail}.requester_id) END";
    private static string RequestedChangeNameSql(string id) => $"(SELECT COALESCE(person.display_name,contact.name) FROM change_request_details detail LEFT JOIN users person ON detail.requester_kind='USER' AND person.id=detail.requester_id LEFT JOIN contacts contact ON detail.requester_kind='CONTACT' AND contact.id=detail.requester_id WHERE detail.change_id={id})";
    private static string SelectedChangeApproverSql(string actor,string id) => $"EXISTS(SELECT 1 FROM change_request_details detail JOIN changes selected_change ON selected_change.id=detail.change_id WHERE detail.change_id={id} AND detail.approver_user_id={actor} AND {ChangePersonScopeSql(actor,"selected_change.client_id")} AND {ChangePersonSiteScopeSql(actor,"detail.site_ids")} AND (selected_change.change_type='STANDARD' OR (selected_change.change_type='NORMAL' AND {actor}<>selected_change.owner_user_id AND {actor}<>selected_change.requested_by_user_id AND {actor}<>COALESCE({RequestedChangeUserSql("detail")},''))))";
    private static string ChangeRequestVisibleToReviewerSql(string actor,string id,string author) => $"({SelectedChangeApproverSql(actor,id)} OR EXISTS(SELECT 1 FROM change_request_details detail JOIN changes scoped_change ON scoped_change.id=detail.change_id WHERE detail.change_id={id} AND {ChangeOrganizationObserverSql(actor,"scoped_change.client_id")} AND {ChangePersonSiteScopeSql(actor,"detail.site_ids")}) OR (NOT EXISTS(SELECT 1 FROM change_request_details detail WHERE detail.change_id={id}) AND {SubmissionReviewerSql(actor,author)}))";
    private static string ChangeRequestDecisionSql(string actor,string id,string author) => $"({SelectedChangeApproverSql(actor,id)} OR (NOT EXISTS(SELECT 1 FROM change_request_details detail WHERE detail.change_id={id}) AND {author}<>{actor} AND NOT EXISTS(SELECT 1 FROM changes owned WHERE owned.id={id} AND owned.owner_user_id={actor}) AND {SubmissionReviewerSql(actor,author)}))";

    public async Task<ChangeReviewPermission> ChangeReviewAccess(string actor,CancellationToken ct)
    {
        var staff=(await FQuery(ChangeReviewRoleSql,ct,("actor",actor))).Count>0;
        var selected=(await FQuery("SELECT c.id FROM changes c WHERE "+SelectedChangeApproverSql("@actor","c.id")+" LIMIT 1",ct,("actor",actor))).Count>0;
        return new(staff||selected,staff);
    }
    public async Task<object> ChangeReviewSession(string actor,CancellationToken ct)
    {
        if(!(await ChangeReviewAccess(actor,ct)).CanReviewChanges)throw new UnauthorizedAccessException();
        return await GetUserView(actor,"chooser",ct)??throw new UnauthorizedAccessException();
    }
    public async Task<object> ChangePeople(string actor,string client,CancellationToken ct)
    {
        await ChangeActor(actor,ct);client=FGuid(client,"organization");
        if(!(await ListClients(actor,ct)).Any(row=>Convert.ToString(row["id"])==client && Convert.ToString(row["status"])=="ACTIVE"))throw new UnauthorizedAccessException();
        var people=await FQuery("SELECT u.id,u.display_name AS name,u.email,IF(EXISTS(SELECT 1 FROM user_roles assigned_role WHERE assigned_role.user_id=u.id AND assigned_role.role_code IN ('ADMIN','MANAGER')) OR (NOT EXISTS(SELECT 1 FROM user_sites WHERE user_id=u.id) AND EXISTS(SELECT 1 FROM user_roles support_role WHERE support_role.user_id=u.id AND support_role.role_code IN ('INTERN','TECHNICIAN','TEAM_LEAD'))),JSON_ARRAY(),COALESCE((SELECT JSON_ARRAYAGG(site_id) FROM user_sites WHERE user_id=u.id),(SELECT JSON_ARRAYAGG(site_id) FROM contacts WHERE user_id=u.id AND active=1 AND site_id IS NOT NULL),JSON_ARRAY())) AS site_ids,EXISTS(SELECT 1 FROM user_roles assigned_role WHERE assigned_role.user_id=u.id AND assigned_role.role_code IN ('INTERN','TECHNICIAN','TEAM_LEAD','MANAGER','ADMIN')) AS can_own FROM users u WHERE "+ChangePersonScopeSql("u.id","@client")+" ORDER BY u.display_name,u.id",ct,("client",client));
        var contacts=await FQuery("SELECT id,name,email,user_id,site_id FROM contacts WHERE client_id=@client AND active=1 ORDER BY name,id",ct,("client",client));
        return new {users=people,contacts};
    }
    private async Task<ChangeRequestSelection> ValidateChangeRequestSelection(MySqlConnection c,MySqlTransaction tx,string actor,string client,string owner,JsonElement body,CancellationToken ct,bool global=false)
    {
        var sites=ChangeSelectionIds(body,"siteIds","siteId",50);var departments=ChangeSelectionIds(body,"departmentIds","departmentId",200);
        foreach(var site in sites)
        {
            if((await FRows(c,tx,"SELECT id FROM sites WHERE id=@site AND client_id=@client AND active=1 AND (@global=1 OR NOT EXISTS(SELECT 1 FROM user_sites WHERE user_id=@actor) OR EXISTS(SELECT 1 FROM user_sites WHERE user_id=@actor AND site_id=@site)) FOR SHARE",ct,("site",site),("client",client),("actor",actor),("global",global))).Count!=1)throw new ArgumentException("Every selected site must be active and within your organization access.");
            await ValidateChangeStaff(c,tx,owner,client,ct,site);
        }
        foreach(var department in departments)if((await FRows(c,tx,"SELECT id FROM departments WHERE id=@id AND active=1 FOR SHARE",ct,("id",department))).Count!=1)throw new ArgumentException("Choose active departments.");
        var requester=JString(body,"requesterRef","USER:"+actor).Split(':',2);
        if(requester.Length!=2||requester[0] is not ("USER" or "CONTACT"))throw new ArgumentException("Choose a requester.");
        var requesterId=FGuid(requester[1],"requester");string? requesterUser=requesterId;
        if(requester[0]=="USER")
        {if((await FRows(c,tx,"SELECT @person AS id WHERE "+ChangePersonScopeSql("@person","@client"),ct,("person",requesterId),("client",client))).Count!=1)throw new ArgumentException("Choose a requester linked to this organization.");}
        else
        {
            var contact=(await FRows(c,tx,"SELECT user_id FROM contacts WHERE id=@id AND client_id=@client AND active=1 FOR SHARE",ct,("id",requesterId),("client",client))).FirstOrDefault()??throw new ArgumentException("Choose a contact linked to this organization.");requesterUser=Convert.ToString(contact["user_id"]);
        }
        var approver=FGuid(JString(body,"approverUserId",JString(body,"changeType")=="STANDARD"?actor:""),"approver");
        var type=JString(body,"changeType");
        if(!ChangeApprovalRule(type,approver,actor,owner,requesterUser,approver))throw new ArgumentException("Normal changes require an approver other than the creator, owner and requester.");
        if((await FRows(c,tx,"SELECT @person AS id WHERE "+ChangePersonScopeSql("@person","@client")+" AND "+ChangePersonSiteScopeSql("@person","CAST(@sites AS JSON)"),ct,("person",approver),("client",client),("sites",FJson(sites)))).Count!=1)throw new ArgumentException("Choose an active approver with access to this organization and all selected sites.");
        return new(sites,departments,requester[0],requesterId,approver);
    }
    private static async Task SaveChangeRequestSelection(MySqlConnection c,MySqlTransaction tx,string id,ChangeRequestSelection selection,CancellationToken ct)
    {
        await FExecute(c,tx,"INSERT INTO change_request_details(change_id,site_ids,department_ids,requester_kind,requester_id,approver_user_id) VALUES(@id,CAST(@sites AS JSON),CAST(@departments AS JSON),@kind,@requester,@approver)",ct,("id",id),("sites",FJson(selection.Sites)),("departments",FJson(selection.Departments)),("kind",selection.RequesterKind),("requester",selection.RequesterId),("approver",selection.Approver));
        foreach(var department in selection.Departments)await FExecute(c,tx,"INSERT INTO change_teams(change_id,department_id,assigned_role,status) VALUES(@id,@department,'IMPLEMENTER','ACTIVE') ON DUPLICATE KEY UPDATE status='ACTIVE'",ct,("id",id),("department",department));
    }
    private static async Task<bool> QueueConfiguredChange(MySqlConnection c,MySqlTransaction tx,string actor,string id,CancellationToken ct)
    {
        var row=(await FRows(c,tx,"SELECT c.client_id,c.change_type,detail.approver_user_id FROM changes c JOIN change_request_details detail ON detail.change_id=c.id WHERE c.id=@id FOR UPDATE",ct,("id",id))).FirstOrDefault();
        if(row is null)return false;
        var approver=Convert.ToString(row["approver_user_id"]);
        if(approver is null || (await FRows(c,tx,"SELECT c.id FROM changes c WHERE c.id=@id AND "+SelectedChangeApproverSql("@approver","c.id"),ct,("id",id),("approver",approver))).Count!=1)throw new ArgumentException("The selected approver is no longer eligible. Check their organization and site access.");
        var auto=(string)row["change_type"]! == "STANDARD" && approver==actor;
        if(!auto && approver==actor)throw new ArgumentException("Normal changes cannot be self-approved.");
        var previous=(await FRows(c,tx,"SELECT revision FROM submission_reviews WHERE kind='CHANGE' AND entity_id=@id FOR UPDATE",ct,("id",id))).FirstOrDefault();var revision=previous is null?1:FInt(previous["revision"])+1;
        await FExecute(c,tx,"INSERT INTO submission_reviews(kind,entity_id,submitted_by_user_id,status,revision,reviewed_by_user_id,reviewed_at,comment) VALUES('CHANGE',@id,@actor,@status,1,IF(@auto,@actor,NULL),IF(@auto,UTC_TIMESTAMP(6),NULL),IF(@auto,'Self-approved Standard change',NULL)) ON DUPLICATE KEY UPDATE submitted_by_user_id=@actor,status=@status,revision=revision+1,reviewed_by_user_id=IF(@auto,@actor,NULL),reviewed_at=IF(@auto,UTC_TIMESTAMP(6),NULL),comment=IF(@auto,'Self-approved Standard change',NULL),submitted_at=UTC_TIMESTAMP(6)",ct,("id",id),("actor",actor),("status",auto?"APPROVED":"PENDING"),("auto",auto));
        await FExecute(c,tx,"UPDATE changes SET status=@status,updated_at=UTC_TIMESTAMP(6) WHERE id=@id",ct,("id",id),("status",auto?"APPROVED":"SUBMITTED"));
        await AddAudit(c,tx,actor,auto?"SUBMISSION.SELF_APPROVED":"SUBMISSION.REVIEW_REQUESTED","CHANGE",id,new{approver,revision,standardSelfApproval=auto},ct);
        var recipients=await FRows(c,tx,"SELECT u.id FROM users u WHERE u.id<>@actor AND ("+ChangeOrganizationObserverSql("u.id","@client")+" AND "+ChangePersonSiteScopeSql("u.id","(SELECT site_ids FROM change_request_details WHERE change_id=@id)")+" OR (@pending=1 AND u.id=@approver))",ct,("actor",actor),("client",row["client_id"]),("id",id),("pending",!auto),("approver",approver));
        foreach(var recipient in recipients)await InsertReviewNotification(c,tx,recipient["id"],"SUBMISSION_REVIEW",auto?"Standard change self-approved":"Change awaiting selected approver","Open Review CM: "+(auto?"Standard change was self-approved. No additional approval is required.":"Only the selected approver can decide this request. TL, managers and administrators are notified for visibility."),ReviewNotificationKey("CHANGE",id,revision,auto?"SELF_APPROVED":"REQUESTED",recipient["id"]),ct);
        return true;
    }
}
