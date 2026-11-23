using System.Globalization;
using System.Text.Json;
using MySqlConnector;

public static partial class FeatureApis
{
    private static void MapChangePlanTemplates(RouteGroupBuilder api)
    {
        api.MapGet("/changes/templates", async (HttpContext c, MySqlStore s, CancellationToken t) => Results.Ok(await s.PublishedChangePlans(Actor(c), t)));
        api.MapPost("/changes/templates", async (JsonElement body, HttpContext c, MySqlStore s, CancellationToken t) => Results.Ok(await s.CreateChangePlan(Actor(c), body, t)));
        api.MapDelete("/changes/templates/{id:guid}", async (string id, HttpContext c, MySqlStore s, CancellationToken t) => Results.Ok(await s.DeleteChangePlan(Actor(c), id, c.Request.Query["source"].ToString(), t)));
        api.MapGet("/changes/template-reviews", async (HttpContext c, MySqlStore s, CancellationToken t) => Results.Ok(await s.ChangePlanReviewQueue(Actor(c), c.Request.Query, t)));
        api.MapGet("/changes/template-reviews/{id:guid}", async (string id, HttpContext c, MySqlStore s, CancellationToken t) => Results.Ok(await s.ChangePlanReviewDetail(Actor(c), id, t)));
        api.MapPost("/changes/templates/{id:guid}/review", async (string id, JsonElement body, HttpContext c, MySqlStore s, CancellationToken t) => Results.Ok(await s.ReviewChangePlan(Actor(c), id, body, t)));
    }
}

public sealed partial class MySqlStore
{
    private const string ChangePlanAuthorSql = "SELECT u.id,EXISTS(SELECT 1 FROM user_roles r WHERE r.user_id=u.id AND r.role_code IN ('TEAM_LEAD','MANAGER','ADMIN')) AS direct_save FROM users u WHERE u.id=@actor AND u.active=1 AND (u.access_expires_at IS NULL OR u.access_expires_at>UTC_TIMESTAMP(6)) AND EXISTS(SELECT 1 FROM user_roles r WHERE r.user_id=u.id AND r.role_code IN ('TECHNICIAN','TEAM_LEAD','MANAGER','ADMIN'))";
    private const string PublishedChangePlansSql = "SELECT t.id,t.name,t.plan,author.display_name AS requested_by_name,reviewer.display_name AS approved_by_name,t.approval_required,t.created_at AS requested_at,t.reviewed_at AS approved_at FROM change_form_templates t JOIN users author ON author.id=t.created_by_user_id LEFT JOIN users reviewer ON reviewer.id=t.reviewed_by_user_id WHERE t.status='APPROVED' ORDER BY t.name,t.id LIMIT 500";

    public async Task<object> DeleteChangePlan(string actor, string id, string source, CancellationToken ct)
    {
        if (source is not ("plan" or "legacy")) throw new ArgumentException("Choose a valid template source.");
        await using var c = CreateConnection(); await c.OpenAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        // Recheck the active role inside the deletion transaction; a hidden button is not authorization.
        if ((await FRows(c,tx,ChangeReviewRoleSql+" FOR SHARE",ct,("actor",actor))).Count==0) throw new UnauthorizedAccessException();
        var table = source=="plan" ? "change_form_templates" : "change_templates";
        var row = (await FRows(c,tx,"SELECT name FROM "+table+" WHERE id=@id"+(source=="legacy"?" AND active=1":"")+" FOR UPDATE",ct,("id",id))).FirstOrDefault() ?? throw new KeyNotFoundException("Template no longer exists. Refresh the list.");
        // Legacy configuration keeps an inactive record; neither path touches any existing change.
        await FExecute(c,tx,source=="plan"?"DELETE FROM change_form_templates WHERE id=@id":"UPDATE change_templates SET active=0,updated_at=UTC_TIMESTAMP(6) WHERE id=@id",ct,("id",id));
        await AddAudit(c,tx,actor,"CHANGE_TEMPLATE.DELETED","CHANGE_TEMPLATE",id,new {name=row["name"],source},ct);
        await tx.CommitAsync(ct); return new {deleted=true};
    }

    private static JsonElement ValidatedChangePlan(JsonElement body)
    {
        var values = new Dictionary<string, object?> {
            ["title"] = FText(JString(body,"title"),"Change title",3,200),
            ["description"] = FText(JString(body,"description"),"Description",3,10000),
            ["category"] = JChoice(body,"category",ChangeCategories),
            ["changeType"] = JChoice(body,"changeType","STANDARD","NORMAL"),
            ["risk"] = JChoice(body,"risk","LOW","MEDIUM","HIGH","CRITICAL"),
            ["outageExpected"] = JBool(body,"outageExpected"),
            ["clientNotificationEnabled"] = JBool(body,"clientNotificationEnabled")
        };
        var downtime = JInt(body,"downtimeMinutes");
        if (downtime is < 0 or > 525600) throw new ArgumentException("Downtime must be between 0 and 525600 minutes.");
        values["downtimeMinutes"] = downtime;
        foreach (var (key, name, min, max) in new[]{("impact","Impact assessment",2,5000),("reason","Reason for change",2,5000),("businessJustification","Business justification",2,5000),("implementationPlan","Implementation plan",2,10000),("testPlan","Testing and validation plan",2,10000),("rollbackPlan","Rollback plan",2,10000),("communicationPlan","Communication plan",0,10000)})
            values[key] = FText(JString(body,key),name,min,max);
        var start = JDate(body,"plannedStart"); var end = JDate(body,"plannedEnd");
        if ((start is null) != (end is null) || end <= start) throw new ArgumentException("Choose a valid planned start and end time.");
        values["plannedStart"] = start?.ToString("O",CultureInfo.InvariantCulture);
        values["plannedEnd"] = end?.ToString("O",CultureInfo.InvariantCulture);
        // Identity, client, owner, contacts, attachments and approval fields never enter the reusable plan.
        return JsonSerializer.SerializeToElement(values);
    }

    public async Task<object> PublishedChangePlans(string actor, CancellationToken ct)
    {
        await using var c = CreateConnection(); await c.OpenAsync(ct);
        if ((await FRows(c,null,ChangePlanAuthorSql,ct,("actor",actor))).Count == 0) throw new UnauthorizedAccessException();
        var items = await FRows(c,null,PublishedChangePlansSql,ct);
        return new { items };
    }

    public async Task<object> CreateChangePlan(string actor, JsonElement body, CancellationToken ct)
    {
        var plan = ValidatedChangePlan(body);
        var name = FText(JString(body,"name",JString(body,"title")),"Template name",2,200);
        await using var c = CreateConnection(); await c.OpenAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var author = (await FRows(c,tx,ChangePlanAuthorSql+" FOR SHARE",ct,("actor",actor))).FirstOrDefault() ?? throw new UnauthorizedAccessException();
        var direct = FBool(author["direct_save"]);
        var id = Guid.NewGuid().ToString("D");
        var status = direct ? "APPROVED" : "PENDING";
        await FExecute(c,tx,"INSERT INTO change_form_templates(id,name,plan,status,approval_required,created_by_user_id,published_at) VALUES(@id,@name,CAST(@plan AS JSON),@status,@required,@actor,IF(@direct,UTC_TIMESTAMP(6),NULL))",ct,("id",id),("name",name),("plan",plan.GetRawText()),("status",status),("required",!direct),("actor",actor),("direct",direct));
        await AddAudit(c,tx,actor,direct?"CHANGE_TEMPLATE.SAVED":"CHANGE_TEMPLATE.SUBMITTED","CHANGE_TEMPLATE",id,new {name,status,directSave=direct},ct);
        if (!direct)
        {
            var reviewers = await FRows(c,tx,"SELECT u.id FROM users u WHERE u.id<>@actor AND "+SubmissionReviewerSql("u.id","@actor"),ct,("actor",actor));
            foreach (var reviewer in reviewers)
                await InsertReviewNotification(c,tx,reviewer["id"],"SUBMISSION_REVIEW","Change template awaiting approval","Open Review CM > Templates: "+name,ReviewNotificationKey("CHANGE_TEMPLATE",id,1,"REQUESTED",reviewer["id"]),ct);
        }
        await tx.CommitAsync(ct);
        return new { id,name,status,approvalRequired=!direct };
    }

    private static string ChangePlanReviewSource => " FROM change_form_templates t JOIN users author ON author.id=t.created_by_user_id LEFT JOIN users reviewer ON reviewer.id=t.reviewed_by_user_id ";
    private static string ChangePlanReviewScope => "t.approval_required=1 AND "+SubmissionReviewerSql("@actor","t.created_by_user_id");
    private static string ChangePlanDecisionScope => "t.id=@id AND t.created_by_user_id<>@actor AND "+ChangePlanReviewScope;
    private static string ChangePlanReviewColumns => "t.id AS entity_id,'Template' AS change_number,t.name AS title,t.status,t.revision,t.created_at AS submitted_at,t.reviewed_at,t.review_comment AS comment,author.display_name AS author_name,reviewer.display_name AS reviewer_name,NULL AS owner_name,'Reusable template' AS client_name,NULL AS site_name,t.status AS change_status,JSON_UNQUOTE(JSON_EXTRACT(t.plan,'$.risk')) AS risk,JSON_UNQUOTE(JSON_EXTRACT(t.plan,'$.changeType')) AS change_type,(t.status='PENDING' AND t.created_by_user_id<>@actor) AS can_review";
    private static string ChangePlanReviewFilter => " WHERE "+ChangePlanReviewScope+" AND (@status IS NULL OR t.status=@status) AND @client IS NULL AND (@from IS NULL OR t.created_at>=@from) AND (@to IS NULL OR t.created_at<@to) AND (@search='' OR LOWER(t.name) LIKE @pattern OR LOWER(JSON_UNQUOTE(JSON_EXTRACT(t.plan,'$.title'))) LIKE @pattern OR LOWER(author.display_name) LIKE @pattern)";

    public async Task<object> ChangePlanReviewQueue(string actor, IQueryCollection query, CancellationToken ct)
    {
        await RequireChangeReviewer(actor,ct);
        var filter = ParseChangeReviewFilters(query);
        var pattern = "%"+filter.Search.ToLowerInvariant().Replace("\\","\\\\").Replace("%","\\%").Replace("_","\\_")+"%";
        var args = new (string,object?)[]{("actor",actor),("status",filter.Status),("client",filter.Client),("from",filter.From),("to",filter.To),("search",filter.Search),("pattern",pattern),("take",filter.PageSize),("skip",(filter.Page-1)*filter.PageSize)};
        var total = Convert.ToInt64((await FQuery("SELECT COUNT(*) AS total"+ChangePlanReviewSource+ChangePlanReviewFilter,ct,args))[0]["total"],CultureInfo.InvariantCulture);
        var items = await FQuery("SELECT "+ChangePlanReviewColumns+ChangePlanReviewSource+ChangePlanReviewFilter+" ORDER BY (t.status='PENDING') DESC,t.created_at DESC,t.id LIMIT @take OFFSET @skip",ct,args);
        return new {items,total,clients=Array.Empty<object>(),page=filter.Page,pageSize=filter.PageSize};
    }

    public async Task<object> ChangePlanReviewDetail(string actor,string id,CancellationToken ct)
    {
        await RequireChangeReviewer(actor,ct);
        var row = (await FQuery("SELECT "+ChangePlanReviewColumns+",t.plan"+ChangePlanReviewSource+" WHERE t.id=@id AND "+ChangePlanReviewScope,ct,("actor",actor),("id",id))).FirstOrDefault() ?? throw new KeyNotFoundException();
        var raw = row["plan"];
        using var parsedPlan = raw is JsonElement ? null : JsonDocument.Parse(Convert.ToString(raw,CultureInfo.InvariantCulture)!);
        var plan = raw is JsonElement json ? json : parsedPlan!.RootElement;
        foreach (var (key,field) in new[]{("title","change_title"),("description","content"),("impact","impact"),("reason","reason"),("businessJustification","business_justification"),("implementationPlan","implementation_plan"),("testPlan","test_plan"),("rollbackPlan","rollback_plan"),("communicationPlan","communication_plan")})
            row[field] = JString(plan,key);
        row["downtime_minutes"] = JInt(plan,"downtimeMinutes");
        row["outage_expected"] = JBool(plan,"outageExpected");
        row["client_notification_enabled"] = JBool(plan,"clientNotificationEnabled");
        row["planned_start"] = JOptional(plan,"plannedStart");
        row["planned_end"] = JOptional(plan,"plannedEnd");
        row.Remove("plan");return row;
    }

    public async Task<object> ReviewChangePlan(string actor,string id,JsonElement body,CancellationToken ct)
    {
        await RequireChangeReviewer(actor,ct);
        var decision = JChoice(body,"decision","APPROVED","REJECTED");
        var comment = FText(JString(body,"comment"),"Review comment",decision=="REJECTED"?3:0,2000);
        await using var c=CreateConnection();await c.OpenAsync(ct);await using var tx=await c.BeginTransactionAsync(ct);
        var row=(await FRows(c,tx,"SELECT t.* FROM change_form_templates t WHERE "+ChangePlanDecisionScope+" FOR UPDATE",ct,("id",id),("actor",actor))).FirstOrDefault() ?? throw new UnauthorizedAccessException();
        if(!SubmissionDecisionCurrent((string?)row["status"],FInt(row["revision"]),JInt(body,"revision")))throw new InvalidOperationException("This template was already reviewed or changed. Refresh before deciding.");
        await FExecute(c,tx,"UPDATE change_form_templates SET status=@status,reviewed_by_user_id=@actor,reviewed_at=UTC_TIMESTAMP(6),review_comment=@comment,published_at=IF(@status='APPROVED',UTC_TIMESTAMP(6),NULL),revision=revision+1 WHERE id=@id",ct,("status",decision),("actor",actor),("comment",comment),("id",id));
        await AddAudit(c,tx,actor,"CHANGE_TEMPLATE."+decision,"CHANGE_TEMPLATE",id,new {decision,comment},ct);
        await InsertReviewNotification(c,tx,row["created_by_user_id"],"SUBMISSION_DECIDED",decision=="APPROVED"?"Change template approved":"Change template needs changes",string.IsNullOrWhiteSpace(comment)?Convert.ToString(row["name"])??"Your template was reviewed.":comment,ReviewNotificationKey("CHANGE_TEMPLATE",id,FInt(row["revision"]),decision,row["created_by_user_id"]),ct);
        await tx.CommitAsync(ct);return new {status=decision};
    }
}
