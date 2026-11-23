using System.Globalization;
using MySqlConnector;

public sealed partial class MySqlStore
{
    private sealed record ProfileActor(bool Admin,bool Staff,bool Wide,bool Team,string WorkScope);
    private sealed record ProfileSource(string From,string Where,string Id,string Title,string Number,string Status,string Review,string Updated,string Usage,string UsageLabel,string Detail,string Note);
    private static readonly string[] ProfileKinds=["CHANGE","PROBLEM","KNOWLEDGE","SERVICE","REQUEST","RESOLVED_TODAY","RESOLVED_30","ASSIGNED","QA"];
    private static string ProfileKind(string kind) => ProfileKinds.Contains(kind)?kind:throw new ArgumentException("Choose a valid profile activity card.");
    private static string ProfileClientScope(string alias) => $"(@admin=1 OR {alias}.client_id IS NULL OR EXISTS(SELECT 1 FROM client_users membership WHERE membership.user_id=@actor AND membership.client_id={alias}.client_id))";
    private static string ProfileTicketScope() => "(@staff=1 AND ("+ProblemTicketVisibilitySql()+"))";
    private static string ProfileOwnRequestScope() => "EXISTS(SELECT 1 FROM contacts own_request WHERE own_request.id=t.requester_id AND own_request.user_id=@actor AND own_request.active=1) AND ("+FScope("t")+")";
    private async Task<ProfileActor> ProfileActorAccess(string actor,CancellationToken ct)
    {
        var row=(await FQuery("SELECT u.work_scope,EXISTS(SELECT 1 FROM user_roles r WHERE r.user_id=u.id AND r.role_code IN ('ADMIN','MANAGER')) AS admin,EXISTS(SELECT 1 FROM user_roles r WHERE r.user_id=u.id AND r.role_code IN ('ADMIN','MANAGER','TEAM_LEAD')) AS wide,EXISTS(SELECT 1 FROM user_roles r WHERE r.user_id=u.id AND r.role_code IN ('ADMIN','MANAGER','TEAM_LEAD','TECHNICIAN','INTERN')) AS staff,EXISTS(SELECT 1 FROM user_permissions p WHERE p.user_id=u.id AND p.permission_code='TICKETS_TEAM_VIEW') AS team FROM users u WHERE u.id=@actor AND u.active=1 AND (u.access_expires_at IS NULL OR u.access_expires_at>UTC_TIMESTAMP(6))",ct,("actor",actor))).FirstOrDefault()??throw new UnauthorizedAccessException();
        var wide=FBool(row["wide"]);var staff=FBool(row["staff"]);var team=wide||FBool(row["team"]);
        if(staff&&!team){await using var c=CreateConnection();await c.OpenAsync(ct);team=await HasDefaultTeamPermission(c,actor,ct);}
        return new(FBool(row["admin"]),staff,wide,team,Convert.ToString(row["work_scope"],CultureInfo.InvariantCulture)??"CLIENT");
    }
    private static ProfileSource ProfileSourceFor(string kind,bool detail)
    {
        ProfileKind(kind);
        if(kind is "CHANGE" or "PROBLEM" or "KNOWLEDGE" or "SERVICE")
        {
            var spec=SubmissionKind(kind);var owner=kind=="CHANGE"?"e.requested_by_user_id":kind=="PROBLEM"?"e.created_by_user_id":kind=="KNOWLEDGE"?"e.author_user_id":"e.created_by_user_id";
            var scope=kind is "CHANGE" or "PROBLEM"?FScope("e"):ProfileClientScope("e");
            if(kind=="CHANGE")scope+=" AND (NOT EXISTS(SELECT 1 FROM change_request_details d WHERE d.change_id=e.id) OR EXISTS(SELECT 1 FROM change_request_details d WHERE d.change_id=e.id AND "+ChangePersonSiteScopeSql("@actor","d.site_ids")+"))";
            var from=" FROM "+spec.Table+" e LEFT JOIN clients cl ON cl.id=e.client_id LEFT JOIN submission_reviews review ON review.kind='"+kind+"' AND review.entity_id=e.id LEFT JOIN users reviewer ON reviewer.id=review.reviewed_by_user_id";
            var usage=kind=="CHANGE"?"(SELECT COUNT(*) FROM change_tasks tasks WHERE tasks.change_id=e.id)":kind=="PROBLEM"?"(SELECT COUNT(DISTINCT links.ticket_id) FROM problem_tickets links JOIN tickets t ON t.id=links.ticket_id WHERE links.problem_id=e.id AND "+ProfileTicketScope()+")":kind=="KNOWLEDGE"?"(SELECT COUNT(DISTINCT links.ticket_id) FROM ticket_knowledge_links links JOIN tickets t ON t.id=links.ticket_id WHERE links.article_id=e.id AND "+ProfileTicketScope()+")":"(SELECT COUNT(DISTINCT history.ticket_id) FROM ticket_history history JOIN tickets t ON t.id=history.ticket_id WHERE history.action='SERVICE.REQUESTED' AND JSON_UNQUOTE(JSON_EXTRACT(history.new_value,'$.templateId'))=e.id AND "+ProfileTicketScope()+")";
            var extra=detail?",e.created_at,e."+spec.Content+" AS content,review.comment AS review_comment,review.reviewed_at,reviewer.display_name AS reviewer_name"+(kind=="CHANGE"?",e.impact,e.implementation_plan,e.test_plan,e.rollback_plan":kind=="PROBLEM"?",e.root_cause,e.workaround,e.resolution":kind=="KNOWLEDGE"?",e.visibility":",e.description"):"";
            return new(from,"@staff=1 AND "+owner+"=@actor AND ("+scope+")","e.id","e."+spec.Title,kind=="CHANGE"?"e.change_number":kind=="PROBLEM"?"e.problem_number":"''",kind=="SERVICE"?"IF(e.active=1,'ACTIVE','INACTIVE')":"e.status","COALESCE(review.status,'DIRECT')","e.updated_at",usage,kind=="CHANGE"?"Work tasks":kind=="SERVICE"?"Recorded requests":"Linked tickets",extra,kind=="SERVICE"?"Service usage counts requests recorded after this update. Older unrecorded uses are not estimated.":"Only your records within your current access are included.");
        }
        var ticketFrom=" FROM tickets t LEFT JOIN clients cl ON cl.id=t.client_id";
        var ticketWhere=ProfileTicketScope();var updated="t.updated_at";var ticketExtra=detail?",t.created_at,t.description AS content,t.priority":"";
        if(kind=="REQUEST")ticketWhere=ProfileOwnRequestScope();
        if(kind=="ASSIGNED")ticketWhere+=" AND t.assigned_user_id=@actor AND t.status NOT IN ('RESOLVED','CLOSED','CANCELLED')";
        if(kind is "RESOLVED_TODAY" or "RESOLVED_30")
        {
            ticketFrom+=" JOIN (SELECT ticket_id,DATE_FORMAT(DATE_ADD(created_at,INTERVAL @offset MINUTE),'%Y-%m-%d') AS resolved_day,MAX(created_at) AS last_resolved FROM ticket_history WHERE actor_user_id=@actor AND action='TICKET.STATUS_CHANGED' AND JSON_UNQUOTE(new_value)='RESOLVED' AND created_at>=@fromUtc AND created_at<@toUtc GROUP BY ticket_id,resolved_day) resolutions ON resolutions.ticket_id=t.id";
            updated="resolutions.last_resolved";ticketExtra+=",resolutions.resolved_day";
        }
        if(kind=="QA")
        {
            ticketFrom+=" JOIN (SELECT ticket_id,new_value,created_at,ROW_NUMBER() OVER(PARTITION BY ticket_id ORDER BY created_at DESC,id DESC) AS rn FROM ticket_history WHERE action='TICKET.QA_REVIEWED') rating ON rating.ticket_id=t.id AND rating.rn=1";
            ticketWhere+=" AND JSON_UNQUOTE(JSON_EXTRACT(rating.new_value,'$.assigneeId'))=@actor AND NULLIF(JSON_UNQUOTE(JSON_EXTRACT(rating.new_value,'$.average')),'null') IS NOT NULL";
            updated="rating.created_at";ticketExtra+=",CAST(NULLIF(JSON_UNQUOTE(JSON_EXTRACT(rating.new_value,'$.average')),'null') AS DECIMAL(8,3))*CASE WHEN JSON_UNQUOTE(JSON_EXTRACT(rating.new_value,'$.ratingScale'))='4' THEN 25 ELSE 20 END AS qa_percent";
        }
        return new(ticketFrom,ticketWhere,"t.id","t.subject","t.ticket_number","t.status","CASE WHEN t.approval_status IN ('PENDING','APPROVED','REJECTED') THEN t.approval_status ELSE 'DIRECT' END",updated,"NULL","",ticketExtra,kind=="REQUEST"?"Tickets where you are the primary requester. Tickets created on behalf of other people are not included.":kind=="RESOLVED_30"?"One row per ticket per day you resolved it, matching the daily average. Only tickets within your current access are shown.":"Only tickets within your current access are shown.");
    }
    private static (string Status,string Review,string Query,int Page,string? Id,int Offset) ProfileActivityFilters(IQueryCollection query)
    {
        var status=FText(query["status"].ToString(),"Status",0,40).ToUpperInvariant();var review=query["review"].ToString();
        if(review is not ("" or "PENDING" or "APPROVED" or "REJECTED" or "DIRECT"))throw new ArgumentException("Choose a valid approval status.");
        var q=FText(query["q"].ToString(),"Search",0,200);var page=int.TryParse(query["page"],out var n)?Math.Clamp(n,1,100000):1;
        var id=query["id"].ToString();var offset=int.TryParse(query["offset"],out var minutes)?Math.Clamp(minutes,-840,840):0;
        return(status,review,q,page,id.Length==0?null:FGuid(id,"record"),offset);
    }
    private static (string,object?)[] ProfileActivityArguments(string actor,ProfileActor access,string kind,int offset,string status="",string review="",string q="",int page=1,string? id=null)
    {
        var localDay=DateTime.UtcNow.AddMinutes(offset).Date;
        var start=kind=="RESOLVED_30"?localDay.AddDays(-29):localDay;
        return [("actor",actor),("id",actor),("admin",access.Admin),("staff",access.Staff),("wide",access.Wide),("team",access.Team),("workScope",access.WorkScope),("offset",offset),("fromUtc",start.AddMinutes(-offset)),("toUtc",localDay.AddDays(1).AddMinutes(-offset)),("status",status),("review",review),("q",q),("pattern","%"+q.ToLowerInvariant().Replace("\\","\\\\").Replace("%","\\%").Replace("_","\\_")+"%"),("record",id),("skip",(page-1)*20)];
    }
    public async Task<object> PersonalActivitySummary(string actor,CancellationToken ct)
    {
        var access=await ProfileActorAccess(actor,ct);var counts=new Dictionary<string,long>();var pending=new Dictionary<string,long>();
        foreach(var kind in new[]{"CHANGE","PROBLEM","KNOWLEDGE","SERVICE","REQUEST"})
        {
            var source=ProfileSourceFor(kind,false);var rows=await FQuery("SELECT COUNT(*) AS total,COALESCE(SUM(("+source.Review+")='PENDING'),0) AS pending"+source.From+" WHERE "+source.Where,ct,ProfileActivityArguments(actor,access,kind,0));counts[kind]=Convert.ToInt64(rows[0]["total"],CultureInfo.InvariantCulture);pending[kind]=Convert.ToInt64(rows[0]["pending"],CultureInfo.InvariantCulture);
        }
        return new{counts,pending};
    }
    public async Task<object> PersonalActivity(string actor,string kind,IQueryCollection query,CancellationToken ct)
    {
        ProfileKind(kind);var access=await ProfileActorAccess(actor,ct);var f=ProfileActivityFilters(query);var source=ProfileSourceFor(kind,f.Id is not null);var args=ProfileActivityArguments(actor,access,kind,f.Offset,f.Status,f.Review,f.Query,f.Page,f.Id);
        var where=" WHERE "+source.Where+" AND (@record IS NULL OR "+source.Id+"=@record) AND (@status='' OR "+source.Status+"=@status) AND (@review='' OR "+source.Review+"=@review) AND (@q='' OR LOWER("+source.Title+") LIKE @pattern OR LOWER("+source.Number+") LIKE @pattern)";
        var total=Convert.ToInt64((await FQuery("SELECT COUNT(*) AS total"+source.From+where,ct,args))[0]["total"],CultureInfo.InvariantCulture);
        var fields="SELECT "+source.Id+" AS id,"+source.Title+" AS title,"+source.Number+" AS number,"+source.Status+" AS status,"+source.Review+" AS review_status,cl.name AS client_name,"+source.Updated+" AS updated_at,"+source.Usage+" AS usage_count"+source.Detail;
        var items=await FQuery(fields+source.From+where+" ORDER BY "+source.Updated+" DESC,"+source.Id+" LIMIT 20 OFFSET @skip",ct,args);
        if(f.Id is not null)return new{item=items.FirstOrDefault()??throw new KeyNotFoundException("This record is no longer within your access."),usageLabel=source.UsageLabel,note=source.Note};
        return new{items,total,page=f.Page,usageLabel=source.UsageLabel,note=source.Note};
    }
    private async Task RecordServiceRequest(MySqlConnection c,MySqlTransaction tx,string actor,string ticket,string? template,string client,CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(template))return;
        template=FGuid(template,"service");
        if((await FRows(c,tx,ProfileServiceTemplateSql(),ct,("template",template),("client",client))).Count==0)throw new ArgumentException("This service is unavailable for the selected client.");
        await RequireSubmissionReleased(c,tx,"SERVICE",template,ct);
        await FExecute(c,tx,ServiceRequestHistorySql,ct,("ticket",ticket),("actor",actor),("template",template));
    }
    private static string ProfileServiceTemplateSql() => "SELECT id FROM service_templates WHERE id=@template AND active=1 AND (client_id IS NULL OR client_id=@client) AND NOT EXISTS(SELECT 1 FROM submission_reviews pending WHERE pending.kind='SERVICE' AND pending.entity_id=service_templates.id AND pending.status<>'APPROVED') FOR SHARE";
    private const string ServiceRequestHistorySql="INSERT INTO ticket_history(ticket_id,actor_user_id,action,new_value) VALUES(@ticket,@actor,'SERVICE.REQUESTED',JSON_OBJECT('templateId',@template))";
}
