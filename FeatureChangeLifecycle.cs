using System.Text.Json;
using MySqlConnector;

public sealed record ReviewedChangeWorkflow(string ReviewStatus, string[] NextStatuses, string RequestedBy, string? ApprovedBy, string? ReviewComment, bool CanAmend, string? Approver = null, string[]? SiteNames = null, string[]? DepartmentNames = null);

public sealed partial class MySqlStore
{
    private const string ApprovedReviewedChangeSql = "UPDATE changes SET status='APPROVED',updated_at=UTC_TIMESTAMP(6) WHERE id=@id";
    private const string MarkReviewedChangeSubmittedSql = "UPDATE changes SET status='SUBMITTED' WHERE id=@id AND EXISTS(SELECT 1 FROM submission_reviews WHERE kind='CHANGE' AND entity_id=@id AND status='PENDING')";
    private static string[] ReviewedChangeNextStatuses(string state, string reviewStatus, bool owner, bool reviewer) =>
        reviewStatus != "APPROVED" ? [] : state switch {
            "DRAFT" or "SUBMITTED" or "ASSESSMENT" or "AWAITING_APPROVAL" or "APPROVED" or "SCHEDULED" when owner => ["IN_PROGRESS"],
            "IN_PROGRESS" or "VALIDATION" when owner => ["COMPLETED", "FAILED", "ROLLED_BACK"],
            "COMPLETED" or "FAILED" or "ROLLED_BACK" when reviewer && !owner => ["CLOSED"],
            _ => []
        };

    private static string ReviewedChangeWorkflowSql => "SELECT r.status,r.comment,COALESCE("+RequestedChangeNameSql("r.entity_id")+",author.display_name) AS requested_by_name,approver.display_name AS approved_by_name,(SELECT selected.display_name FROM change_request_details detail JOIN users selected ON selected.id=detail.approver_user_id WHERE detail.change_id=r.entity_id) AS selected_approver,(r.submitted_by_user_id<>@actor AND @owner<>@actor AND (EXISTS(SELECT 1 FROM change_request_details detail JOIN changes scoped_change ON scoped_change.id=detail.change_id WHERE detail.change_id=r.entity_id AND "+ChangeOrganizationObserverSql("@actor","scoped_change.client_id")+") OR (NOT EXISTS(SELECT 1 FROM change_request_details detail WHERE detail.change_id=r.entity_id) AND "+SubmissionReviewerSql("@actor","r.submitted_by_user_id")+"))) AS can_close FROM submission_reviews r JOIN users author ON author.id=r.submitted_by_user_id LEFT JOIN users approver ON approver.id=r.reviewed_by_user_id WHERE r.kind='CHANGE' AND r.entity_id=@id";

    private static async Task<ReviewedChangeWorkflow?> ReadReviewedChangeWorkflow(MySqlConnection c, MySqlTransaction? tx, string actor, Dictionary<string,object?> change, CancellationToken ct)
    {
        var row=(await FRows(c,tx,ReviewedChangeWorkflowSql+(tx is null?"":" FOR UPDATE"),ct,("actor",actor),("owner",change["owner_user_id"]),("id",change["id"]))).FirstOrDefault();
        if(row is null)return null;
        var status=(string)row["status"]!;
        var context=(await FRows(c,tx,"SELECT site_ids,department_ids FROM change_request_details WHERE change_id=@id",ct,("id",change["id"]))).FirstOrDefault();
        var siteNames=context is null?[]:(await FRows(c,tx,"SELECT name FROM sites WHERE JSON_CONTAINS(CAST(@ids AS JSON),JSON_QUOTE(id)) ORDER BY name",ct,("ids",context["site_ids"] is JsonElement sites?sites.GetRawText():context["site_ids"]))).Select(row=>Convert.ToString(row["name"])!).ToArray();
        var departmentNames=context is null?[]:(await FRows(c,tx,"SELECT name FROM departments WHERE JSON_CONTAINS(CAST(@ids AS JSON),JSON_QUOTE(id)) ORDER BY name",ct,("ids",context["department_ids"] is JsonElement departments?departments.GetRawText():context["department_ids"]))).Select(row=>Convert.ToString(row["name"])!).ToArray();
        var state=(string)change["status"]!;var owner=Equals(change["owner_user_id"],actor);
        return new(status,ReviewedChangeNextStatuses(state,status,owner,FBool(row["can_close"])),(string)row["requested_by_name"]!,status=="APPROVED"?row["approved_by_name"] as string:null,row["comment"] as string,owner && state is "DRAFT" or "SUBMITTED" or "ASSESSMENT" or "AWAITING_APPROVAL" or "APPROVED" or "SCHEDULED",row["selected_approver"] as string,siteNames,departmentNames);
    }

    private async Task<bool> ApplyReviewedChangeStatus(MySqlConnection c, MySqlTransaction tx, string actor, Dictionary<string,object?> change, string next, JsonElement body, CancellationToken ct)
    {
        var workflow=await ReadReviewedChangeWorkflow(c,tx,actor,change,ct);
        if(workflow is null)return false; // Retain the established governance flow for older, unreviewed records.
        if(!workflow.NextStatuses.Contains(next))throw new UnauthorizedAccessException("This workflow action is not available to your account at the current stage.");
        var id=(string)change["id"]!;
        var note=FText(JString(body,"note"),next=="CLOSED"?"Closure review":"Work results",next=="IN_PROGRESS"?0:10,10000);
        if(next=="IN_PROGRESS")
        {
            // Review CM is the authorization for this technician workflow. A saved template never supplies it.
            if(change["planned_start"] is not null || change["planned_end"] is not null)
            {
                if(change["planned_start"] is not DateTime start || change["planned_end"] is not DateTime end || end<=start)throw new ArgumentException("Choose a valid planned window before starting work.");
                var conflicts=await ChangeConflicts(c,tx,change,ct);
                if(conflicts.Changes.Count>0||conflicts.Blackouts.Count>0)throw new ArgumentException("The planned window conflicts with another change or a maintenance blackout. Ask your reviewer to revise the schedule before starting.");
            }
        }
        if(next=="COMPLETED" && (await FRows(c,tx,"SELECT id FROM change_tasks WHERE change_id=@id AND status NOT IN ('COMPLETED','WAIVED') LIMIT 1",ct,("id",id))).Count>0)throw new ArgumentException("Complete all assigned tasks before marking the change completed.");
        if(next is "COMPLETED" or "FAILED" or "ROLLED_BACK")
        {
            await FExecute(c,tx,"INSERT INTO change_updates(id,change_id,author_user_id,update_type,body) VALUES(@update,@id,@actor,'VALIDATION',@body)",ct,("update",Guid.NewGuid().ToString("D")),("id",id),("actor",actor),("body",note));
            var reviewers=await FRows(c,tx,"SELECT u.id FROM users u WHERE u.id<>@author AND u.id<>@owner AND (EXISTS(SELECT 1 FROM change_request_details WHERE change_id=@id) AND "+ChangeOrganizationObserverSql("u.id","@client")+" OR NOT EXISTS(SELECT 1 FROM change_request_details WHERE change_id=@id) AND "+SubmissionReviewerSql("u.id","@author")+")",ct,("author",change["requested_by_user_id"]),("owner",change["owner_user_id"]),("client",change["client_id"]),("id",id));
            foreach(var reviewer in reviewers)
                await NotifyChange(c,tx,Convert.ToString(reviewer["id"])!,id,"SUBMISSION_REVIEW","Change ready for closure: "+change["change_number"],"Implementation results are ready. Review the completed work and close the change.",ReviewNotificationKey("CHANGE_WORKFLOW",id,FInt(change["revision"]),next,reviewer["id"]),ct);
        }
        if(next=="CLOSED")
        {
            var state=(string)change["status"]!;
            var outcome=state=="COMPLETED"?"SUCCESS":state=="ROLLED_BACK"?"ROLLED_BACK":"FAILED";
            var reviewId=Guid.NewGuid().ToString("D");
            await FExecute(c,tx,"INSERT INTO change_reviews(id,change_id,revision,outcome,summary,follow_up,actor_user_id) VALUES(@review,@id,@revision,@outcome,@summary,@followup,@actor)",ct,("review",reviewId),("id",id),("revision",change["revision"]),("outcome",outcome),("summary",note),("followup",JOptional(body,"followUp")??"No follow-up required."),("actor",actor));
            await ChangeEvent(c,tx,actor,id,"CHANGE.REVIEW_RECORDED",new {reviewId,outcome,revision=change["revision"]},ct);
            foreach(var user in new[]{Convert.ToString(change["owner_user_id"])!,Convert.ToString(change["requested_by_user_id"])!}.Distinct())
                await NotifyChange(c,tx,user,id,"SUBMISSION_DECIDED","Change closed: "+change["change_number"],note,ReviewNotificationKey("CHANGE_WORKFLOW",id,FInt(change["revision"]),"CLOSED",user),ct);
        }
        await FExecute(c,tx,"UPDATE changes SET status=@status,actual_start=IF(@status='IN_PROGRESS',COALESCE(actual_start,UTC_TIMESTAMP(6)),actual_start),actual_end=IF(@status IN ('COMPLETED','FAILED','ROLLED_BACK','CLOSED'),COALESCE(actual_end,UTC_TIMESTAMP(6)),actual_end),updated_at=UTC_TIMESTAMP(6) WHERE id=@id",ct,("status",next),("id",id));
        return true;
    }
}
