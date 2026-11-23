using System.Globalization;
using MySqlConnector;

public static partial class FeatureApis
{
    private static void MapDeliveryReviews(RouteGroupBuilder api) => api.MapGet("/delivery-reviews/{kind}", async (string kind,HttpContext c,MySqlStore s,CancellationToken t) => Results.Ok(await s.DeliveryReviews(Actor(c),kind,c.Request.Query,t)));
}

public sealed partial class MySqlStore
{
    private static bool ServiceActivationAllowed(bool requested,bool technician,bool previousReview) => requested && !technician && !previousReview;
    private static string DeliveryReviewScope(string kind,string actor,string author,string client,string site)
    {
        var scope=KnowledgeReviewScope(actor,author,client);
        if(kind=="PROBLEM")scope+=$" AND (EXISTS(SELECT 1 FROM user_roles dr WHERE dr.user_id={actor} AND dr.role_code IN ('ADMIN','MANAGER')) OR NOT EXISTS(SELECT 1 FROM user_sites ds WHERE ds.user_id={actor}) OR EXISTS(SELECT 1 FROM user_sites ds WHERE ds.user_id={actor} AND ds.site_id={site}))";
        return scope;
    }

    private static string ProblemSubmissionDescription(CreateProblemRequest body)
    {
        var description=FText(body.Description??"","Description",3,10000);
        foreach(var (name,value) in new[]{("Observed recurrence",body.Recurrence),("Business impact",body.BusinessImpact)})
        {
            var text=FText(value??"",name,0,2000);
            if(text.Length>0)description+="\n\n"+name+":\n"+text;
        }
        if(description.Length>10000)throw new ArgumentException("Description, recurrence and business impact must total 10,000 characters or less.");
        return description;
    }

    public async Task<object> DeliveryReviews(string actor,string kind,IQueryCollection query,CancellationToken ct)
    {
        if(kind is not ("PROBLEM" or "SERVICE"))throw new ArgumentException("Choose Problem Management or Service Catalogue.");
        if((await FQuery(ChangeReviewRoleSql,ct,("actor",actor))).Count==0)throw new UnauthorizedAccessException();
        var spec=SubmissionKind(kind);
        var status=query["status"].ToString();if(status.Length==0)status="PENDING";
        if(status is not ("PENDING" or "APPROVED" or "REJECTED" or "ALL"))throw new ArgumentException("Choose a valid review status.");
        var q=query["q"].ToString().Trim();if(q.Length>200)throw new ArgumentException("Search is too long.");
        var client=query["client"].ToString();if(client.Length>0&&!Guid.TryParse(client,out _))throw new ArgumentException("Choose a valid client.");
        DateTime? ReadDate(string key){var value=query[key].ToString();if(value.Length==0)return null;if(!DateTime.TryParseExact(value,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var date)||date.Year>9998)throw new ArgumentException("Choose a valid review date.");return date;}
        var from=ReadDate("from");var through=ReadDate("to");if(from>through)throw new ArgumentException("The start date must be before the end date.");var to=through?.AddDays(1);
        var page=int.TryParse(query["page"],out var n)?Math.Clamp(n,1,100000):1;
        var scope=" FROM submission_reviews r JOIN "+spec.Table+" e ON e.id=r.entity_id JOIN users u ON u.id=r.submitted_by_user_id LEFT JOIN clients cl ON cl.id=e.client_id WHERE r.kind=@kind AND "+DeliveryReviewScope(kind,"@actor","r.submitted_by_user_id","e.client_id",kind=="PROBLEM"?"e.site_id":"NULL")+" AND (@status='ALL' OR r.status=@status) AND (@client='' OR e.client_id=@client) AND (@from IS NULL OR r.submitted_at>=@from) AND (@to IS NULL OR r.submitted_at<@to) AND (@q='' OR LOWER(e."+spec.Title+") LIKE @pattern OR LOWER(u.display_name) LIKE @pattern)";
        var args=new (string,object?)[]{("actor",actor),("kind",kind),("status",status),("client",client),("from",from),("to",to),("q",q),("pattern","%"+q.ToLowerInvariant().Replace("\\","\\\\").Replace("%","\\%").Replace("_","\\_")+"%"),("skip",(page-1)*20)};
        var total=Convert.ToInt64((await FQuery("SELECT COUNT(*) AS total"+scope,ct,args))[0]["total"],CultureInfo.InvariantCulture);
        var extra=kind=="PROBLEM"?",e.root_cause,e.workaround,e.resolution,e.priority,(SELECT COUNT(*) FROM problem_tickets pt WHERE pt.problem_id=e.id) AS linked_ticket_count":",e.description";
        var items=await FQuery("SELECT r.entity_id,r.status,r.revision,r.submitted_at,r.reviewed_at,r.comment,e."+spec.Title+" AS title,e."+spec.Content+" AS content,u.display_name AS author_name,cl.name AS client_name,(r.status='PENDING' AND r.submitted_by_user_id<>@actor) AS can_review"+extra+scope+" ORDER BY r.submitted_at DESC,r.entity_id LIMIT 20 OFFSET @skip",ct,args);
        return new{items,total,page};
    }
}
