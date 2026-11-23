using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using MySqlConnector;

public sealed class KnowledgeUpload
{
    public string Name { get; init; } = "";
    public string Data { get; init; } = "";
}

public sealed class KnowledgeDetails
{
    public string Scope { get; init; } = "GENERAL";
    public string ArticleType { get; init; } = "HOW_TO";
    public string Product { get; init; } = "";
    public string Version { get; init; } = "";
    public string Environment { get; init; } = "ALL";
    public string Prerequisites { get; init; } = "";
    public string Keywords { get; init; } = "";
    public string ReviewDate { get; init; } = "";
    public string ContentTitle { get; init; } = "";
    public string ErrorMessage { get; init; } = "";
}

public static partial class FeatureApis
{
    private static void MapKnowledgeReview(RouteGroupBuilder api)
    {
        api.MapGet("/knowledge-reviews", async (HttpContext c, MySqlStore s, CancellationToken t) => Results.Ok(await s.KnowledgeReviews(Actor(c), c.Request.Query, t)));
        api.MapGet("/knowledge/{id:guid}/files/{file}", async (string id, string file, HttpContext c, MySqlStore s, CancellationToken t) =>
        {
            var asset = await s.KnowledgeFile(Actor(c), id, file, t);
            c.Response.Headers.CacheControl = "private, no-store";
            c.Response.Headers["X-Content-Type-Options"] = "nosniff";
            if (asset.Type.StartsWith("image/", StringComparison.Ordinal)) return Results.File(asset.Bytes, asset.Type);
            return Results.File(asset.Bytes, "application/octet-stream", asset.Name);
        });
    }
}

public sealed partial class MySqlStore
{
    private const string KnowledgeMediaMarker = "\n\n[ASTRA_KB_MEDIA_V1]";
    private const string KnowledgeDetailsMarker = "[ASTRA_KB_DETAILS_V1]";
    private const string KnowledgeTextMarker = "\n[ASTRA_KB_TEXT]\n";
    private sealed record KnowledgeAsset(string Id, string Name, string Type, int Size);
    private sealed record KnowledgePrepared(string Body, List<string> Created);
    private static readonly JsonSerializerOptions KnowledgeJson = new(JsonSerializerDefaults.Web);
    private static KnowledgeDetails? ReadKnowledgeDetails(string body)
    {
        if (!body.StartsWith(KnowledgeDetailsMarker,StringComparison.Ordinal)) return null;
        var end=body.IndexOf(KnowledgeTextMarker,StringComparison.Ordinal);
        if(end<0)return null;
        try{return JsonSerializer.Deserialize<KnowledgeDetails>(body[KnowledgeDetailsMarker.Length..end],KnowledgeJson);}catch(JsonException){return null;}
    }
    private static KnowledgeDetails ValidateKnowledgeDetails(KnowledgeDetails input,string? client)
    {
        if(input.Scope is not ("GENERAL" or "CLIENT_SPECIFIC"))throw new ArgumentException("Choose a valid article scope.");
        if(input.Scope=="CLIENT_SPECIFIC" && client is null)throw new ArgumentException("Choose a client for client-specific guidance.");
        if(input.ArticleType is not ("HOW_TO" or "TROUBLESHOOTING" or "FAQ" or "KNOWN_ISSUE" or "PROCEDURE"))throw new ArgumentException("Choose a valid article type.");
        if(input.Environment is not ("ALL" or "PRODUCTION" or "TEST" or "DEVELOPMENT" or "STAGING"))throw new ArgumentException("Choose a valid environment.");
        var date=(input.ReviewDate??"").Trim();
        if(date.Length>0 && !DateTime.TryParseExact(date,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out _))throw new ArgumentException("Choose a valid review date.");
        return new KnowledgeDetails{Scope=input.Scope,ArticleType=input.ArticleType,Product=FText(input.Product??"","Product or service",0,200),Version=FText(input.Version??"","Version",0,150),Environment=input.Environment,Prerequisites=FText(input.Prerequisites??"","Prerequisites",0,2000),Keywords=FText(input.Keywords??"","Keywords",0,500),ReviewDate=date,ContentTitle=FText(input.ContentTitle??"","Content title",0,300),ErrorMessage=FText(input.ErrorMessage??"","Error message",0,2000)};
    }
    private static string KnowledgeReviewScope(string actor, string author, string client) =>
        SubmissionReviewerSql(actor, author) + $" AND (EXISTS(SELECT 1 FROM user_roles kr WHERE kr.user_id={actor} AND kr.role_code IN ('ADMIN','MANAGER')) OR ({client} IS NOT NULL AND EXISTS(SELECT 1 FROM client_users kc WHERE kc.user_id={actor} AND kc.client_id={client})))";

    private static List<KnowledgeAsset> KnowledgeAssets(string body)
    {
        var marker = body.LastIndexOf(KnowledgeMediaMarker, StringComparison.Ordinal);
        if (marker < 0) return [];
        try { return JsonSerializer.Deserialize<List<KnowledgeAsset>>(body[(marker + KnowledgeMediaMarker.Length)..], KnowledgeJson) ?? []; }
        catch (JsonException) { return []; }
    }

    private static string KnowledgeDirectory(string id) => Path.Combine(AppContext.BaseDirectory, "App_Data", "knowledge", Guid.Parse(id).ToString("D"));
    private static string KnowledgeImageType(byte[] bytes)
    {
        if (bytes.Length >= 8 && bytes.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10})) return "image/png";
        if (bytes.Length >= 3 && bytes[0]==255 && bytes[1]==216 && bytes[2]==255) return "image/jpeg";
        if (bytes.Length >= 6 && (System.Text.Encoding.ASCII.GetString(bytes,0,6) is "GIF87a" or "GIF89a")) return "image/gif";
        if (bytes.Length >= 12 && System.Text.Encoding.ASCII.GetString(bytes,0,4)=="RIFF" && System.Text.Encoding.ASCII.GetString(bytes,8,4)=="WEBP") return "image/webp";
        return "application/octet-stream";
    }

    private static async Task<KnowledgePrepared> PrepareKnowledgeBody(string id, string text, string oldBody, KnowledgeArticleRequest request, CancellationToken ct)
    {
        if (text.Contains(KnowledgeMediaMarker, StringComparison.Ordinal) || text.Contains(KnowledgeDetailsMarker,StringComparison.Ordinal) || text.Contains(KnowledgeTextMarker,StringComparison.Ordinal)) throw new ArgumentException("Use the article fields and file controls to add details and attachments.");
        var details=request.Details ?? ReadKnowledgeDetails(oldBody);
        var heading=details is null ? "" : KnowledgeDetailsMarker+JsonSerializer.Serialize(ValidateKnowledgeDetails(details,request.ClientId),KnowledgeJson)+KnowledgeTextMarker;
        var old = KnowledgeAssets(oldBody);
        var retained = request.RetainedAttachments ?? [];
        if (retained.Any(x => !old.Any(a => a.Id == x))) throw new ArgumentException("An attachment changed. Reload the article.");
        var assets = old.Where(x => retained.Contains(x.Id)).DistinctBy(x => x.Id).ToList();
        var uploads = request.Attachments ?? [];
        if (assets.Count + uploads.Length > 10) throw new ArgumentException("Attach up to 10 files per article.");
        var pending = new List<(KnowledgeAsset Asset, byte[] Bytes)>();
        long total = assets.Sum(x => (long)x.Size);
        foreach (var upload in uploads)
        {
            if (upload.Data.Length > 7_000_000) throw new ArgumentException("Each file must be 5 MB or smaller.");
            byte[] bytes;
            try { bytes = Convert.FromBase64String(upload.Data); } catch (FormatException) { throw new ArgumentException("An attachment could not be read."); }
            if (bytes.Length is < 1 or > 5_242_880) throw new ArgumentException("Each file must be between 1 byte and 5 MB.");
            total += bytes.Length;
            if (total > 10_485_760) throw new ArgumentException("Article attachments must total 10 MB or less.");
            var name = upload.Name.Replace('\\','/').Split('/').Last().Trim();
            if (name.Length is < 1 or > 180 || name.Any(char.IsControl)) throw new ArgumentException("Choose a valid attachment filename.");
            var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var asset = new KnowledgeAsset(hash, name, KnowledgeImageType(bytes), bytes.Length);
            if (!assets.Any(x => x.Id == hash)) { assets.Add(asset); pending.Add((asset, bytes)); }
        }
        var result = heading + text + (assets.Count == 0 ? "" : KnowledgeMediaMarker + JsonSerializer.Serialize(assets, KnowledgeJson));
        if (result.Length > 50000) throw new ArgumentException("Article text and file descriptions must fit within 50,000 characters.");
        var created = new List<string>();
        try
        {
            if (pending.Count > 0) Directory.CreateDirectory(KnowledgeDirectory(id));
            foreach (var item in pending)
            {
                var path = Path.Combine(KnowledgeDirectory(id), item.Asset.Id);
                if (!File.Exists(path)) { await File.WriteAllBytesAsync(path, item.Bytes, ct); created.Add(path); }
            }
            return new(result, created);
        }
        catch { foreach (var path in created) File.Delete(path); throw; }
    }

    public async Task<object> KnowledgeReviews(string actor, IQueryCollection query, CancellationToken ct)
    {
        if ((await FQuery(ChangeReviewRoleSql, ct, ("actor", actor))).Count == 0) throw new UnauthorizedAccessException();
        var status = query["status"].ToString();
        if (status.Length == 0) status = "PENDING";
        if (status is not ("PENDING" or "APPROVED" or "REJECTED" or "ALL")) throw new ArgumentException("Choose a valid review status.");
        var search = query["q"].ToString().Trim(); if (search.Length > 200) throw new ArgumentException("Search is too long.");
        var client = query["client"].ToString();
        if (client.Length > 0 && !Guid.TryParse(client, out _)) throw new ArgumentException("Choose a valid organization.");
        var page = int.TryParse(query["page"], out var n) ? Math.Clamp(n,1,100000) : 1;
        DateTime? ReadDate(string key) { var value=query[key].ToString(); if(value.Length==0)return null; if(!DateTime.TryParseExact(value,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var date) || date.Year>9998)throw new ArgumentException("Choose a valid review date.");return date; }
        var from=ReadDate("from");var through=ReadDate("to");if(from>through)throw new ArgumentException("The start date must be before the end date.");var to=through?.AddDays(1);
        var scope = " FROM submission_reviews r JOIN knowledge_articles e ON e.id=r.entity_id JOIN users u ON u.id=r.submitted_by_user_id LEFT JOIN clients cl ON cl.id=e.client_id WHERE r.kind='KNOWLEDGE' AND " + KnowledgeReviewScope("@actor","r.submitted_by_user_id","e.client_id") + " AND (@status='ALL' OR r.status=@status) AND (@client='' OR e.client_id=@client) AND (@from IS NULL OR r.submitted_at>=@from) AND (@to IS NULL OR r.submitted_at<@to) AND (@q='' OR LOWER(e.title) LIKE @pattern OR LOWER(u.display_name) LIKE @pattern)";
        var args = new (string,object?)[]{("actor",actor),("status",status),("client",client),("from",from),("to",to),("q",search),("pattern","%"+search.ToLowerInvariant().Replace("\\","\\\\").Replace("%","\\%").Replace("_","\\_")+"%"),("skip",(page-1)*20)};
        var total = Convert.ToInt64((await FQuery("SELECT COUNT(*) AS total"+scope, ct,args))[0]["total"],CultureInfo.InvariantCulture);
        var items = await FQuery("SELECT r.entity_id,r.status,r.revision,r.submitted_at,r.reviewed_at,r.comment,e.title,e.body AS content,e.visibility,u.display_name AS author_name,cl.name AS client_name,(r.status='PENDING' AND r.submitted_by_user_id<>@actor) AS can_review"+scope+" ORDER BY r.submitted_at DESC,r.entity_id LIMIT 20 OFFSET @skip",ct,args);
        return new {items,total,page};
    }

    public async Task<(byte[] Bytes,string Type,string Name)> KnowledgeFile(string actor,string id,string file,CancellationToken ct)
    {
        if (file.Length!=64 || file.Any(c => !Uri.IsHexDigit(c))) throw new KeyNotFoundException();
        string? body = null;
        var article = await ListKnowledge(actor,"",1,id,ct);
        if (article is not null)
        {
            var json = JsonSerializer.SerializeToElement(article);
            if (json.TryGetProperty("body",out var value)) body = value.GetString();
        }
        if (body is null)
        {
            var row = (await FQuery("SELECT e.body FROM knowledge_articles e JOIN submission_reviews r ON r.entity_id=e.id AND r.kind='KNOWLEDGE' WHERE e.id=@id AND "+KnowledgeReviewScope("@actor","r.submitted_by_user_id","e.client_id"),ct,("actor",actor),("id",id))).FirstOrDefault();
            body = row is null ? null : Convert.ToString(row["body"]);
        }
        var asset = body is null ? null : KnowledgeAssets(body).FirstOrDefault(x => x.Id==file);
        if (asset is null) throw new KeyNotFoundException();
        var path = Path.Combine(KnowledgeDirectory(id),asset.Id);
        if (!File.Exists(path)) throw new KeyNotFoundException("Attachment is unavailable.");
        return (await File.ReadAllBytesAsync(path,ct),asset.Type,asset.Name);
    }
}
