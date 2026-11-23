using System.Globalization;
using Microsoft.AspNetCore.Http;

public sealed record ChangeReviewFilters(string? Status, string Search, string? Client, DateTime? From, DateTime? To, int Page, int PageSize);

public sealed partial class MySqlStore
{
    private static ChangeReviewFilters ParseChangeReviewFilters(IQueryCollection query)
    {
        var status = string.IsNullOrWhiteSpace(query["status"]) ? "PENDING" : query["status"].ToString();
        if (status is not ("ALL" or "PENDING" or "APPROVED" or "REJECTED")) throw new ArgumentException("Choose a valid review status.");
        var search = FText(query["q"], "Search", 0, 200);
        var client = string.IsNullOrWhiteSpace(query["clientId"]) ? null : FGuid(query["clientId"], "MSP");
        DateTime? Boundary(string name)
        {
            if (string.IsNullOrWhiteSpace(query[name])) return null;
            if (!DateTimeOffset.TryParse(query[name], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value))
                throw new ArgumentException("Choose a valid submitted date.");
            return value.UtcDateTime;
        }
        var from = Boundary("from"); var to = Boundary("to");
        if (from is not null && to is not null && to <= from) throw new ArgumentException("The end date must follow the start date.");
        var page = int.TryParse(query["page"], out var p) ? Math.Clamp(p, 1, 100000) : 1;
        var size = int.TryParse(query["pageSize"], out var n) ? Math.Clamp(n, 1, 100) : 20;
        return new(status == "ALL" ? null : status, search, client, from, to, page, size);
    }

    private async Task RequireChangeReviewer(string actor, CancellationToken ct)
    {
        if ((await FQuery(ChangeReviewRoleSql, ct, ("actor", actor))).Count == 0 &&
            (await FQuery("SELECT c.id FROM changes c WHERE "+SelectedChangeApproverSql("@actor","c.id")+" LIMIT 1",ct,("actor",actor))).Count==0)
            throw new UnauthorizedAccessException();
    }

    private const string ChangeReviewRoleSql = "SELECT u.id FROM users u WHERE u.id=@actor AND u.active=1 AND (u.access_expires_at IS NULL OR u.access_expires_at>UTC_TIMESTAMP(6)) AND EXISTS(SELECT 1 FROM user_roles r WHERE r.user_id=u.id AND r.role_code IN ('TEAM_LEAD','MANAGER','ADMIN'))";
    private static string ChangeReviewSource => " FROM submission_reviews r JOIN changes c ON c.id=r.entity_id JOIN users author ON author.id=r.submitted_by_user_id JOIN clients cl ON cl.id=c.client_id LEFT JOIN sites site ON site.id=c.site_id LEFT JOIN users reviewer ON reviewer.id=r.reviewed_by_user_id LEFT JOIN users owner ON owner.id=c.owner_user_id ";
    private static string ChangeReviewScope => "r.kind='CHANGE' AND " + ChangeRequestVisibleToReviewerSql("@actor","r.entity_id","r.submitted_by_user_id");
    private static string ChangeReviewFilterSql => " WHERE " + ChangeReviewScope + " AND (@status IS NULL OR r.status=@status) AND (@client IS NULL OR c.client_id=@client) AND (@from IS NULL OR r.submitted_at>=@from) AND (@to IS NULL OR r.submitted_at<@to) AND (@search='' OR LOWER(c.change_number) LIKE @pattern OR LOWER(c.title) LIKE @pattern OR LOWER(author.display_name) LIKE @pattern)";
    private static string ChangeReviewColumns => "r.entity_id,r.status,r.revision,r.submitted_at,r.reviewed_at,r.comment,c.change_number,c.title,c.client_id,c.site_id,c.status AS change_status,c.risk,c.change_type,COALESCE("+RequestedChangeNameSql("c.id")+",author.display_name) AS author_name,reviewer.display_name AS reviewer_name,owner.display_name AS owner_name,cl.name AS client_name,site.name AS site_name,(r.status='PENDING' AND "+ChangeRequestDecisionSql("@actor","c.id","r.submitted_by_user_id")+") AS can_review";

    public async Task<object> ChangeReviewQueue(string actor, IQueryCollection query, CancellationToken ct)
    {
        await RequireChangeReviewer(actor, ct);
        var filter = ParseChangeReviewFilters(query);
        var pattern = "%" + filter.Search.ToLowerInvariant().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
        var args = new (string, object?)[] { ("actor", actor), ("status", filter.Status), ("search", filter.Search), ("pattern", pattern), ("client", filter.Client), ("from", filter.From), ("to", filter.To), ("take", filter.PageSize), ("skip", (filter.Page - 1) * filter.PageSize) };
        var where = ChangeReviewFilterSql;
        var total = Convert.ToInt64((await FQuery("SELECT COUNT(*) AS total" + ChangeReviewSource + where, ct, args))[0]["total"], CultureInfo.InvariantCulture);
        var items = await FQuery("SELECT " + ChangeReviewColumns + ChangeReviewSource + where + " ORDER BY (r.status='PENDING') DESC,r.submitted_at DESC,r.entity_id LIMIT @take OFFSET @skip", ct, args);
        var clients = await FQuery("SELECT DISTINCT cl.id,cl.name" + ChangeReviewSource + " WHERE " + ChangeReviewScope + " ORDER BY cl.name", ct, ("actor", actor));
        return new { items, total, clients, page = filter.Page, pageSize = filter.PageSize };
    }

    public async Task<object> ChangeReviewDetail(string actor, string id, CancellationToken ct)
    {
        await RequireChangeReviewer(actor, ct);
        var row=(await FQuery("SELECT " + ChangeReviewColumns + ",c.description AS content,c.impact,c.reason,c.business_justification,c.implementation_plan,c.test_plan,c.rollback_plan,c.communication_plan,c.planned_start,c.planned_end" + ChangeReviewSource + " WHERE r.entity_id=@id AND " + ChangeReviewScope, ct, ("actor", actor), ("id", id))).FirstOrDefault() ?? throw new KeyNotFoundException();
        var context=(await FQuery("SELECT detail.*,person.display_name AS approver_name FROM change_request_details detail LEFT JOIN users person ON person.id=detail.approver_user_id WHERE detail.change_id=@id",ct,("id",id))).FirstOrDefault();
        if(context is not null)
        {
            row["selected_approver_name"]=context["approver_name"];
            foreach(var (field,table,target) in new[]{("site_ids","sites","site_names"),("department_ids","departments","department_names")})
            {
                var values=FStrings(context[field]);
                var names=await FQuery("SELECT name FROM "+table+" WHERE JSON_CONTAINS(CAST(@ids AS JSON),JSON_QUOTE(id)) ORDER BY name",ct,("ids",FJson(values)));
                row[target]=string.Join(", ",names.Select(name=>Convert.ToString(name["name"])));
            }
        }
        return row;
    }
}
