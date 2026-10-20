using Astra.Api.Infrastructure;
using Astra.Api.Security;
using Astra.Api.Modules;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<Database>();
builder.AddAstraIdentity();
builder.Services.AddHttpClient("entra-directory");
builder.Services.AddSingleton(sp => new EntraDirectory(
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("entra-directory"),
    sp.GetRequiredService<IdentityOptions>()));
builder.Services.AddScoped<ServiceReports>();
builder.Services.AddScoped<Administration>();
builder.Services.AddScoped<Groups>();
builder.Services.AddScoped<ClientDomains>();
builder.Services.AddScoped<CoreData>();
builder.Services.AddScoped<Notifications>();
builder.Services.AddScoped<SlaConfiguration>();
builder.Services.AddScoped<TicketRead>();
builder.Services.AddScoped<Problems>();
builder.Services.AddScoped<TicketWrite>();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    options.SerializerOptions.UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow;
});
var app = builder.Build();
app.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers.XContentTypeOptions = "nosniff";
    try { await next(context); }
    catch (Exception exception)
    {
        var status = exception switch { ApiFault fault => fault.Status, BadHttpRequestException bad => bad.StatusCode, Microsoft.Data.SqlClient.SqlException sql when sql.Number is 2601 or 2627 => 409, Microsoft.Data.SqlClient.SqlException sql when sql.Number == 547 => 400, _ => 503 };
        var reference = Guid.NewGuid();
        app.Logger.LogError("Request failed {Reference} {ErrorType}", reference, exception.GetType().Name);
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(new { error = new { code = "HTTP_" + status, message = exception is ApiFault ? exception.Message : status == 400 ? "Invalid request or linked records." : status == 409 ? "A conflicting record already exists." : "Service unavailable", requestId = reference } });
    }
});
app.UseAuthentication();
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? "/";
    if (path is "/health" or "/healthz" or "/ready" or "/auth/status" or "/auth/login" or "/auth/callback") { await next(context); return; }
    var auth = context.RequestServices.GetRequiredService<AuthService>();
    var actor = await auth.CurrentAsync(context);
    WorkspacePolicy.RequireOperational(actor, context.Request.Method, path);
    if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method) && !HttpMethods.IsOptions(context.Request.Method)) await auth.VerifyCsrfAsync(context);
    await next(context);
});
app.MapAstraIdentity();
ServiceReports.Map(app);
Administration.Map(app);
Groups.Map(app);
ClientDomains.Map(app);
CoreData.Map(app);
Notifications.Map(app);
SlaConfiguration.Map(app);
TicketRead.Map(app);
Problems.Map(app);
TicketWrite.Map(app);
app.MapGet("/healthz", () => Results.Json(new { status = "ok", runtime = "aspnetcore", release = "migration" }));
app.MapGet("/health", () => Results.Json(new { status = "ok" }));
app.MapGet("/ready", async (Database database, ISessionStore sessions, CancellationToken cancellationToken) =>
{
    await using var connection = await database.OpenAsync(cancellationToken);
    var result = await Database.QueryAsync(connection, "SELECT version FROM dbo.app_schema_state WHERE id=1 AND version>=24", cancellationToken: cancellationToken);
    await sessions.CheckAsync();
    return result.Count == 1 ? Results.Json(new { status = "ready" }) : Results.Json(new { error = new { code = "HTTP_503", message = "Service unavailable" } }, statusCode: 503);
});
app.Run();

public partial class Program;
