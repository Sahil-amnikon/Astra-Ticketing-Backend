using System.Text.Json;
using MySqlConnector;

public static partial class FeatureApis
{
    private static void MapSubmissionReviews(RouteGroupBuilder api)
    {
        api.MapGet("/changes/reviews", async (HttpContext c, MySqlStore s, CancellationToken t) => Results.Ok(await s.ChangeReviewQueue(Actor(c), c.Request.Query, t)));
        api.MapGet("/changes/reviews/{id:guid}", async (string id, HttpContext c, MySqlStore s, CancellationToken t) => Results.Ok(await s.ChangeReviewDetail(Actor(c), id, t)));
        api.MapGet("/submission-reviews/{kind}", async (string kind, HttpContext c, MySqlStore s, CancellationToken t) => Results.Ok(await s.SubmissionReviews(Actor(c), kind, t)));
        api.MapPost("/submission-reviews/{kind}/{id:guid}/decision", async (string kind, string id, JsonElement body, HttpContext c, MySqlStore s, CancellationToken t) => Results.Ok(await s.DecideSubmission(Actor(c), kind, id, body, t)));
        MapChangePlanTemplates(api);
        MapKnowledgeReview(api);
        MapDeliveryReviews(api);
        MapProblemTicketLinks(api);
    }
}

public sealed partial class MySqlStore
{
    private const string ReviewNotificationInsertSql = "INSERT INTO notifications(user_id,type,title,body,event_key) VALUES(@user,@type,@title,@body,@key)";
    // Reuse the installed notification vocabulary; review state stays in its
    // review tables and titles/bodies retain the precise module and decision.
    private static string StoredReviewNotificationType(string type) => type switch
    {
        "SUBMISSION_REVIEW" => "CHANGE_APPROVAL_REQUESTED",
        "SUBMISSION_DECIDED" => "CHANGE_STATUS_UPDATED",
        _ => type
    };
    private static string ReviewNotificationKey(string kind, string id, int revision, string phase, object? user) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            $"review:{kind}:{id}:{revision}:{phase}:{Convert.ToString(user, System.Globalization.CultureInfo.InvariantCulture)}")));
    private static Task<int> InsertReviewNotification(MySqlConnection c, MySqlTransaction tx, object? user, string type, string title, string body, string key, CancellationToken ct) =>
        FExecute(c, tx, ReviewNotificationInsertSql, ct, ("user", user), ("type", StoredReviewNotificationType(type)), ("title", title), ("body", body), ("key", key));

    // Keep team routing independent of an employee's shared Amnikon email domain.
    private static string SubmissionReviewerSql(string reviewer, string author) => $@"EXISTS(SELECT 1 FROM users review_user WHERE review_user.id={reviewer} AND review_user.active=1
        AND (review_user.access_expires_at IS NULL OR review_user.access_expires_at>UTC_TIMESTAMP(6))
        AND (EXISTS(SELECT 1 FROM user_roles rr WHERE rr.user_id=review_user.id AND rr.role_code IN ('ADMIN','MANAGER'))
          OR (EXISTS(SELECT 1 FROM user_roles rr WHERE rr.user_id=review_user.id AND rr.role_code='TEAM_LEAD')
            AND (EXISTS(SELECT 1 FROM client_users mine JOIN client_users theirs ON theirs.client_id=mine.client_id
                WHERE mine.user_id=review_user.id AND theirs.user_id={author} AND mine.client_id<>'00000000-0000-4000-8000-000000000132')
              OR (EXISTS(SELECT 1 FROM client_users mine WHERE mine.user_id=review_user.id AND mine.client_id='00000000-0000-4000-8000-000000000132')
                AND NOT EXISTS(SELECT 1 FROM client_users mine WHERE mine.user_id=review_user.id AND mine.client_id<>'00000000-0000-4000-8000-000000000132')
                AND EXISTS(SELECT 1 FROM client_users theirs WHERE theirs.user_id={author} AND theirs.client_id='00000000-0000-4000-8000-000000000132')
                AND NOT EXISTS(SELECT 1 FROM client_users theirs WHERE theirs.user_id={author} AND theirs.client_id<>'00000000-0000-4000-8000-000000000132'))))))";

    private static string SubmissionVisibleSql(string kind, string entity, string actor) => $@"(NOT EXISTS(SELECT 1 FROM submission_reviews sr WHERE sr.kind='{kind}' AND sr.entity_id={entity} AND sr.status<>'APPROVED')
        OR EXISTS(SELECT 1 FROM submission_reviews sr WHERE sr.kind='{kind}' AND sr.entity_id={entity} AND (sr.submitted_by_user_id={actor} OR {(kind=="CHANGE"?ChangeRequestVisibleToReviewerSql(actor,entity,"sr.submitted_by_user_id")+" OR EXISTS(SELECT 1 FROM changes owned WHERE owned.id="+entity+" AND owned.owner_user_id="+actor+")":SubmissionReviewerSql(actor,"sr.submitted_by_user_id"))})))";

    private static (string Table, string Title, string Content) SubmissionKind(string kind) => kind switch
    {
        "CHANGE" => ("changes", "title", "description"),
        "PROBLEM" => ("problems", "title", "description"),
        "KNOWLEDGE" => ("knowledge_articles", "title", "body"),
        "SERVICE" => ("service_templates", "name", "instructions"),
        _ => throw new ArgumentException("Choose a valid submission type.")
    };

    private static async Task<bool> TechnicianSubmission(MySqlConnection c, MySqlTransaction? tx, string actor, CancellationToken ct) =>
        (await FRows(c, tx, "SELECT u.id FROM users u WHERE u.id=@actor AND u.active=1 AND (u.access_expires_at IS NULL OR u.access_expires_at>UTC_TIMESTAMP(6)) AND EXISTS(SELECT 1 FROM user_roles r WHERE r.user_id=u.id AND r.role_code='TECHNICIAN') AND NOT EXISTS(SELECT 1 FROM user_roles r WHERE r.user_id=u.id AND r.role_code IN ('TEAM_LEAD','MANAGER','ADMIN'))", ct, ("actor", actor))).Count != 0;

    // Called while the entity is locked. Every edit invalidates the previous review version.
    private static async Task QueueSubmission(MySqlConnection c, MySqlTransaction tx, string actor, string kind, string id, CancellationToken ct)
    {
        if(kind=="CHANGE" && await QueueConfiguredChange(c,tx,actor,id,ct))return;
        var technician = await TechnicianSubmission(c, tx, actor, ct);
        var previous = (await FRows(c, tx, "SELECT * FROM submission_reviews WHERE kind=@kind AND entity_id=@id FOR UPDATE", ct, ("kind", kind), ("id", id))).FirstOrDefault();
        if (!technician && previous is null) return;
        await FExecute(c, tx, "INSERT INTO submission_reviews(kind,entity_id,submitted_by_user_id,status,revision) VALUES(@kind,@id,@actor,'PENDING',1) ON DUPLICATE KEY UPDATE status='PENDING',revision=revision+1,reviewed_by_user_id=NULL,reviewed_at=NULL,comment=NULL,submitted_at=UTC_TIMESTAMP(6)", ct, ("kind", kind), ("id", id), ("actor", actor));
        var author = previous?.GetValueOrDefault("submitted_by_user_id") ?? actor;
        var revision = previous is null ? 1 : FInt(previous["revision"]) + 1;
        await AddAudit(c, tx, actor, "SUBMISSION.REVIEW_REQUESTED", kind, id, new { kind }, ct);
        var deliveryTable = kind == "PROBLEM" ? "problems" : "service_templates";
        var recipientScope = kind is "PROBLEM" or "SERVICE" ? DeliveryReviewScope(kind,"u.id","@author","(SELECT d.client_id FROM "+deliveryTable+" d WHERE d.id=@entity)",kind=="PROBLEM"?"(SELECT d.site_id FROM problems d WHERE d.id=@entity)":"NULL") : kind == "KNOWLEDGE" ? KnowledgeReviewScope("u.id", "@author", "(SELECT k.client_id FROM knowledge_articles k WHERE k.id=@entity)") : SubmissionReviewerSql("u.id", "@author");
        var recipients = await FRows(c, tx, "SELECT u.id FROM users u WHERE u.id<>@author AND " + recipientScope, ct, ("author", author), ("entity", id));
        if (kind is "KNOWLEDGE" or "PROBLEM" or "SERVICE" && recipients.Count == 0) throw new InvalidOperationException("No active team lead, manager or administrator can review this submission. Ask an administrator to assign a reviewer to your organization.");
        foreach (var recipient in recipients)
            await InsertReviewNotification(c, tx, recipient["id"], "SUBMISSION_REVIEW", "Submission awaiting review", kind == "PROBLEM" ? "A problem is awaiting approval. Open Problem Management > Review PM." : kind == "SERVICE" ? "A service is awaiting approval. Open Service catalogue > Review services." : kind == "KNOWLEDGE" ? "An article is awaiting approval. Open Knowledge base > Review KB." : kind == "CHANGE" ? "A change submission is awaiting review. Open Change Management > Review CM." : $"A {kind.ToLowerInvariant()} submission is awaiting review. Open its Review submissions panel.", ReviewNotificationKey(kind,id,revision,"REQUESTED",recipient["id"]), ct);
    }

    public async Task<object> SubmissionReviews(string actor, string kind, CancellationToken ct)
    {
        var spec = SubmissionKind(kind);
        var fields = kind == "CHANGE" ? ",e.impact,e.implementation_plan,e.test_plan,e.rollback_plan,e.risk,e.change_type" : kind == "PROBLEM" ? ",e.root_cause,e.workaround,e.resolution,e.priority" : kind == "KNOWLEDGE" ? ",e.visibility" : ",e.description";
        var reviewerScope = kind is "PROBLEM" or "SERVICE" ? DeliveryReviewScope(kind,"@actor","r.submitted_by_user_id","e.client_id",kind=="PROBLEM"?"e.site_id":"NULL") : kind == "KNOWLEDGE" ? KnowledgeReviewScope("@actor", "r.submitted_by_user_id", "e.client_id") : SubmissionReviewerSql("@actor", "r.submitted_by_user_id");
        var rows = await FQuery($@"SELECT r.*,e.{spec.Title} AS title,e.{spec.Content} AS content,cl.name AS client_name,u.display_name AS author_name,
            (r.submitted_by_user_id<>@actor AND {reviewerScope}) AS can_review{fields}
            FROM submission_reviews r JOIN {spec.Table} e ON e.id=r.entity_id JOIN users u ON u.id=r.submitted_by_user_id LEFT JOIN clients cl ON cl.id=e.client_id
            WHERE r.kind=@kind AND (r.submitted_by_user_id=@actor OR {reviewerScope})
            ORDER BY (r.status='PENDING') DESC,r.submitted_at DESC LIMIT 100", ct, ("actor", actor), ("kind", kind));
        return new { items = rows };
    }

    public async Task<object> DecideSubmission(string actor, string kind, string id, JsonElement body, CancellationToken ct)
    {
        var spec = SubmissionKind(kind);
        var decision = JChoice(body, "decision", "APPROVED", "REJECTED");
        var comment = FText(JString(body, "comment"), "Review comment", decision == "REJECTED" ? 3 : 0, 2000);
        await using var c = CreateConnection(); await c.OpenAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        if ((await FRows(c, tx, $"SELECT id FROM {spec.Table} WHERE id=@id FOR UPDATE", ct, ("id", id))).Count == 0) throw new KeyNotFoundException();
        var decisionScope = kind=="CHANGE" ? ChangeRequestDecisionSql("@actor","r.entity_id","r.submitted_by_user_id") : "r.submitted_by_user_id<>@actor AND "+ (kind=="KNOWLEDGE" ? KnowledgeReviewScope("@actor","r.submitted_by_user_id","(SELECT k.client_id FROM knowledge_articles k WHERE k.id=r.entity_id)") : SubmissionReviewerSql("@actor","r.submitted_by_user_id"));
        if(kind is "PROBLEM" or "SERVICE")decisionScope="r.submitted_by_user_id<>@actor AND "+DeliveryReviewScope(kind,"@actor","r.submitted_by_user_id","(SELECT d.client_id FROM "+spec.Table+" d WHERE d.id=r.entity_id)",kind=="PROBLEM"?"(SELECT d.site_id FROM problems d WHERE d.id=r.entity_id)":"NULL");
        var review = (await FRows(c, tx, "SELECT r.* FROM submission_reviews r WHERE r.kind=@kind AND r.entity_id=@id AND " + decisionScope + " FOR UPDATE", ct, ("kind", kind), ("id", id), ("actor", actor))).FirstOrDefault() ?? throw new UnauthorizedAccessException();
        if (!SubmissionDecisionCurrent((string?)review["status"], FInt(review["revision"]), JInt(body, "revision"))) throw new InvalidOperationException("This submission changed or was already reviewed. Refresh before deciding.");
        await FExecute(c, tx, "UPDATE submission_reviews SET status=@status,reviewed_by_user_id=@actor,reviewed_at=UTC_TIMESTAMP(6),comment=@comment,revision=revision+1 WHERE kind=@kind AND entity_id=@id", ct, ("status", decision), ("actor", actor), ("comment", comment), ("kind", kind), ("id", id));
        if (decision == "APPROVED")
        {
            // Change release remains subject to the existing CAB, risk and scheduling approvals.
            var sql = kind switch {
                "KNOWLEDGE" => "UPDATE knowledge_articles SET status='PUBLISHED',revision=revision+1,updated_at=UTC_TIMESTAMP(6) WHERE id=@id",
                "SERVICE" => "UPDATE service_templates SET active=1,updated_at=UTC_TIMESTAMP(6) WHERE id=@id",
                "CHANGE" => ApprovedReviewedChangeSql,
                _ => "UPDATE problems SET updated_at=UTC_TIMESTAMP(6) WHERE id=@id"
            };
            await FExecute(c, tx, sql, ct, ("id", id));
        }
        await AddAudit(c, tx, actor, "SUBMISSION." + decision, kind, id, new { decision, comment, revision = review["revision"] }, ct);
        if(kind=="CHANGE")await ChangeEvent(c,tx,actor,id,"CHANGE."+decision,new {comment,reviewRevision=review["revision"]},ct);
        await InsertReviewNotification(c, tx, review["submitted_by_user_id"], "SUBMISSION_DECIDED", decision == "APPROVED" ? "Submission approved" : "Submission needs changes", string.IsNullOrEmpty(comment) ? "Your submission was reviewed." : comment, ReviewNotificationKey(kind,id,FInt(review["revision"]),decision,review["submitted_by_user_id"]), ct);
        await tx.CommitAsync(ct);
        return new { status = decision };
    }

    private static bool SubmissionDecisionCurrent(string? status, int revision, int expected) => status == "PENDING" && revision > 0 && revision == expected;

    private static async Task RequireSubmissionReleased(MySqlConnection c, MySqlTransaction tx, string kind, string id, CancellationToken ct)
    {
        if ((await FRows(c, tx, "SELECT entity_id FROM submission_reviews WHERE kind=@kind AND entity_id=@id AND status<>'APPROVED'", ct, ("kind", kind), ("id", id))).Count > 0)
            throw new ArgumentException("This submission must be approved before continuing the workflow.");
    }
}
