using System.Globalization;
using MySqlConnector;
using System.Text.Json;

public static partial class FeatureApis
{
    private static void MapProblemTicketLinks(RouteGroupBuilder api)
    {
        api.MapGet("/problems/ticket-options",async(HttpContext c,MySqlStore s,CancellationToken t)=>Results.Ok(await s.ProblemTicketOptions(Actor(c),c.Request.Query,t)));
        api.MapPost("/problems/{id:guid}/tickets/batch",async(string id,JsonElement body,HttpContext c,MySqlStore s,CancellationToken t)=>Results.Ok(await s.AddProblemTicketBatch(Actor(c),id,body,t)));
    }
}

public sealed partial class MySqlStore
{
    private sealed record ProblemTicketAccess(bool Admin,bool Wide,bool Team,string WorkScope);
    private static string[] ProblemTicketIds(string[]? ids)
    {
        var result=(ids??[]).Select(id=>FGuid(id,"ticket")).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if(result.Length>100)throw new ArgumentException("A problem can link up to 100 tickets.");
        return result;
    }
    private async Task<ProblemTicketAccess> ProblemTicketGrants(MySqlConnection c,string actor,CancellationToken ct)
    {
        var row=(await FRows(c,null,"SELECT u.work_scope,EXISTS(SELECT 1 FROM user_roles r WHERE r.user_id=u.id AND r.role_code IN ('ADMIN','MANAGER')) AS admin,EXISTS(SELECT 1 FROM user_roles r WHERE r.user_id=u.id AND r.role_code IN ('ADMIN','MANAGER','TEAM_LEAD')) AS wide,EXISTS(SELECT 1 FROM user_permissions p WHERE p.user_id=u.id AND p.permission_code='TICKETS_TEAM_VIEW') AS team FROM users u WHERE u.id=@id AND u.active=1 AND (u.access_expires_at IS NULL OR u.access_expires_at>UTC_TIMESTAMP(6)) AND EXISTS(SELECT 1 FROM user_roles r WHERE r.user_id=u.id AND r.role_code IN ('TECHNICIAN','TEAM_LEAD','MANAGER','ADMIN'))",ct,("id",actor))).FirstOrDefault()??throw new UnauthorizedAccessException();
        var wide=FBool(row["wide"]);var team=wide||FBool(row["team"]);
        if(!team)team=await HasDefaultTeamPermission(c,actor,ct);
        return new(FBool(row["admin"]),wide,team,Convert.ToString(row["work_scope"],CultureInfo.InvariantCulture)??"CLIENT");
    }
    private static string ProblemTicketVisibilitySql() => "(@admin=1 OR ((EXISTS(SELECT 1 FROM client_users cu WHERE cu.user_id=@id AND cu.client_id=t.client_id) OR EXISTS(SELECT 1 FROM user_sites us WHERE us.user_id=@id AND us.site_id=t.site_id)) AND (NOT EXISTS(SELECT 1 FROM user_sites us WHERE us.user_id=@id) OR EXISTS(SELECT 1 FROM user_sites us WHERE us.user_id=@id AND us.site_id=t.site_id)))) AND ("+InternalTicketVisibility("t","@id")+") AND (@wide=1 OR (@team=1 AND (NOT EXISTS(SELECT 1 FROM user_sites us WHERE us.user_id=@id) OR EXISTS(SELECT 1 FROM user_sites us WHERE us.user_id=@id AND us.site_id=t.site_id))) OR (@team=0 AND @workScope IN ('INTERNAL','BOTH','ALL') AND t.client_id='00000000-0000-4000-8000-000000000132' AND (NOT EXISTS(SELECT 1 FROM department_users du WHERE du.user_id=@id) OR EXISTS(SELECT 1 FROM department_users du WHERE du.user_id=@id AND du.department_id=t.department_id))) OR (@team=0 AND @workScope NOT IN ('INTERNAL','BOTH','ALL') AND (t.assigned_user_id=@id OR t.created_by_user_id=@id OR EXISTS(SELECT 1 FROM ticket_collaborators tc WHERE tc.ticket_id=t.id AND tc.user_id=@id))))";
    private static string ProblemTicketSearchSql() => "EXISTS(SELECT 1 FROM problem_tickets pt JOIN tickets t ON t.id=pt.ticket_id WHERE pt.problem_id=p.id AND t.client_id=p.client_id AND (t.ticket_number LIKE @pattern OR t.subject LIKE @pattern OR t.description LIKE @pattern) AND "+ProblemTicketVisibilitySql()+")";
    private static string InitialProblemTicketCheckSql() => "SELECT t.id FROM tickets t WHERE t.id=@ticket AND t.client_id=@client AND (@site IS NULL OR t.site_id=@site) AND "+ProblemTicketVisibilitySql()+" FOR SHARE";
    private const string ProblemTicketInsertSql="INSERT IGNORE INTO problem_tickets(problem_id,ticket_id,client_id,linked_by_user_id) VALUES(@problem,@ticket,@client,@actor)";
    private static void AddProblemTicketParameters(MySqlCommand command,ProblemTicketAccess access)
    {
        command.Parameters.AddWithValue("@wide",access.Wide);command.Parameters.AddWithValue("@team",access.Team);command.Parameters.AddWithValue("@workScope",access.WorkScope);
    }
    private async Task AttachInitialProblemTickets(MySqlConnection c,MySqlTransaction tx,string actor,string problem,string client,string? site,string[] ids,ProblemTicketAccess access,CancellationToken ct)
    {
        foreach(var ticket in ids)
        {
            await using var check=new MySqlCommand(InitialProblemTicketCheckSql(),c,tx);
            check.Parameters.AddWithValue("@ticket",ticket);check.Parameters.AddWithValue("@client",client);check.Parameters.AddWithValue("@site",(object?)site??DBNull.Value);check.Parameters.AddWithValue("@id",actor);check.Parameters.AddWithValue("@admin",access.Admin);AddProblemTicketParameters(check,access);
            if(await check.ExecuteScalarAsync(ct) is null)throw new ArgumentException("A selected ticket is unavailable or outside the chosen client/site. Refresh the ticket selection.");
            var changed=await FExecute(c,tx,ProblemTicketInsertSql,ct,("problem",problem),("ticket",ticket),("client",client),("actor",actor));
            if(changed==0)continue;
            await FExecute(c,tx,"INSERT INTO problem_history(id,problem_id,actor_user_id,action,details) VALUES(@id,@problem,@actor,'PROBLEM.TICKET_LINKED',JSON_OBJECT('ticketId',@ticket))",ct,("id",Guid.NewGuid().ToString("D")),("problem",problem),("actor",actor),("ticket",ticket));
            await AddAudit(c,tx,actor,"PROBLEM.TICKET_LINKED","PROBLEM",problem,new{ticketId=ticket},ct);
        }
    }
    public async Task<object> ProblemTicketOptions(string actor,IQueryCollection query,CancellationToken ct)
    {
        var rawClient=query["clientId"].ToString();var client=rawClient.Length==0?null:FGuid(rawClient,"client");var site=JOptionalSite(query["siteId"].ToString());
        var search=FText(query["q"].ToString(),"Ticket search",0,200);var page=int.TryParse(query["page"],out var n)?Math.Clamp(n,1,100000):1;
        await using var c=CreateConnection();await c.OpenAsync(ct);var access=await ProblemTicketGrants(c,actor,ct);
        var predicate=ProblemTicketOptionsFromSql();
        var status=FText(query["status"].ToString(),"Status",0,40);
        var args=new(string,object?)[]{("client",client),("site",site),("id",actor),("actor",actor),("admin",access.Admin),("wide",access.Wide),("team",access.Team),("workScope",access.WorkScope),("search",search),("status",status),("pattern","%"+search.Replace("\\","\\\\").Replace("%","\\%").Replace("_","\\_")+"%"),("skip",(page-1)*25)};
        var total=Convert.ToInt64((await FRows(c,null,"SELECT COUNT(*) AS total"+predicate,ct,args))[0]["total"],CultureInfo.InvariantCulture);
        var items=await FRows(c,null,"SELECT t.id,t.ticket_number,t.subject,t.status,t.site_id,t.client_id,cl.name AS client_name,(cl.status='ACTIVE') AS client_active"+predicate+" ORDER BY t.created_at DESC,t.id LIMIT 25 OFFSET @skip",ct,args);
        if(query["includeProblems"]=="true"&&items.Count>0)
        {
            var linkedArgs=args.ToList();var slots=new List<string>();
            for(var index=0;index<items.Count;index++){var name="ticket"+index;slots.Add("@"+name);linkedArgs.Add((name,Convert.ToString(items[index]["id"],CultureInfo.InvariantCulture)));}
            var links=await FRows(c,null,"SELECT pt.ticket_id,p.id,p.problem_number,p.title,p.client_id,p.site_id,p.status,(@wide=1 OR (p.owner_user_id=@id AND p.status<>'CLOSED')) AS can_edit,(p.status<>'CLOSED' AND (@wide=1 OR @team=1 OR p.owner_user_id=@id)) AS can_link FROM problem_tickets pt JOIN problems p ON p.id=pt.problem_id JOIN tickets t ON t.id=pt.ticket_id WHERE pt.ticket_id IN ("+string.Join(',',slots)+") AND t.client_id=p.client_id AND "+ProblemTicketVisibilitySql()+" AND "+ProblemLinkedVisibilitySql()+" ORDER BY p.updated_at DESC,p.id",ct,linkedArgs.ToArray());
            foreach(var item in items)item["problems"]=links.Where(link=>string.Equals(Convert.ToString(link["ticket_id"],CultureInfo.InvariantCulture),Convert.ToString(item["id"],CultureInfo.InvariantCulture),StringComparison.OrdinalIgnoreCase)).ToList();
        }
        return new{items,total,page};
    }
    private static string ProblemTicketOptionsFromSql() => " FROM tickets t JOIN clients cl ON cl.id=t.client_id WHERE (@client IS NULL OR t.client_id=@client) AND (@site IS NULL OR t.site_id=@site) AND "+ProblemTicketVisibilitySql()+" AND (@status='' OR t.status=@status) AND (@search='' OR t.ticket_number LIKE @pattern OR t.subject LIKE @pattern OR t.description LIKE @pattern)";
    private static string ProblemLinkedVisibilitySql() => "("+SubmissionVisibleSql("PROBLEM","p.id","@id")+") AND "+FScope("p");
    private static string ProblemBatchParentSql() => "SELECT p.id,p.client_id,p.site_id,p.owner_user_id,p.status FROM problems p WHERE p.id=@problem AND "+ProblemLinkedVisibilitySql()+" AND (@wide=1 OR @team=1 OR p.owner_user_id=@id) FOR UPDATE";
    private static bool ProblemBatchStatusAllowed(string status) => status!="CLOSED";
    private static bool ProblemOwnerIsActor(object? owner,string actor) => string.Equals(Convert.ToString(owner,CultureInfo.InvariantCulture),actor,StringComparison.OrdinalIgnoreCase);
    public async Task<object> AddProblemTicketBatch(string actor,string problem,JsonElement body,CancellationToken ct)
    {
        if(!body.TryGetProperty("ticketIds",out var list)||list.ValueKind!=JsonValueKind.Array)throw new ArgumentException("Choose tickets to attach.");
        var ids=ProblemTicketIds(list.EnumerateArray().Select(value=>value.ValueKind==JsonValueKind.String?value.GetString()!:throw new ArgumentException("Choose valid tickets.")).ToArray());
        if(ids.Length==0)throw new ArgumentException("Choose at least one ticket.");
        await using var c=CreateConnection();await c.OpenAsync(ct);var access=await ProblemTicketGrants(c,actor,ct);await using var tx=await c.BeginTransactionAsync(ct);
        var parent=(await FRows(c,tx,ProblemBatchParentSql(),ct,("problem",problem),("actor",actor),("id",actor),("admin",access.Admin),("wide",access.Wide),("team",access.Team))).FirstOrDefault()??throw new UnauthorizedAccessException();
        if(!ProblemBatchStatusAllowed(Convert.ToString(parent["status"])??""))throw new ArgumentException("Reopen the problem before adding tickets.");
        var existing=await FRows(c,tx,"SELECT ticket_id FROM problem_tickets WHERE problem_id=@problem",ct,("problem",problem));
        var oldIds=existing.Select(row=>Convert.ToString(row["ticket_id"],CultureInfo.InvariantCulture)!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if(oldIds.Union(ids,StringComparer.OrdinalIgnoreCase).Count()>100)throw new ArgumentException("A problem can link up to 100 tickets.");
        await AttachInitialProblemTickets(c,tx,actor,problem,Convert.ToString(parent["client_id"],CultureInfo.InvariantCulture)!,Convert.ToString(parent["site_id"],CultureInfo.InvariantCulture) is string site&&site.Length>0?site:null,ids,access,ct);
        var added=ids.Count(id=>!oldIds.Contains(id));
        if(added>0){await FExecute(c,tx,"UPDATE problems SET revision=revision+1,updated_at=UTC_TIMESTAMP(6) WHERE id=@problem",ct,("problem",problem));await FExecute(c,tx,"UPDATE submission_reviews SET revision=revision+1 WHERE kind='PROBLEM' AND entity_id=@problem AND status='PENDING'",ct,("problem",problem));}
        await tx.CommitAsync(ct);return new{added};
    }
    private static string? JOptionalSite(string value) => value.Length==0?null:FGuid(value,"site");
}
