$ErrorActionPreference = 'Stop'
$backend = Split-Path $PSScriptRoot -Parent
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('astra-policy-tests-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($scratch) | Out-Null
try {
  $policy = [Security.SecurityElement]::Escape((Join-Path $backend 'AuthSessionPolicy.cs'))
  $audit = [Security.SecurityElement]::Escape((Join-Path $backend 'ProblemAuditDetails.cs'))
  @"
<Project Sdk="Microsoft.NET.Sdk.Web"><PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup><ItemGroup><Compile Include="$policy" Link="AuthSessionPolicy.cs"/><Compile Include="$audit" Link="ProblemAuditDetails.cs"/></ItemGroup></Project>
"@ | Set-Content -LiteralPath (Join-Path $scratch 'Checks.csproj') -Encoding utf8
  @'
using System.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
static void Check(bool condition, string label) { if (!condition) throw new Exception(label); }
var configuration = new ConfigurationBuilder().Build();
var lifetime = AuthSessionPolicy.Lifetime(configuration);
Check(lifetime == TimeSpan.FromHours(8), "Default session must last eight hours");
var clock = new ManualClock();
var properties = AuthSessionPolicy.SignIn(lifetime, clock.GetUtcNow());
Check(properties.AllowRefresh == true && !properties.IsPersistent && properties.ExpiresUtc == clock.GetUtcNow().AddHours(8), "Sign-in lifetime mismatch");
var before = new Dictionary<string, object?> { ["title"]="Same title", ["description"]="Old description", ["status"]="OPEN", ["ownerUserId"]="old-owner", ["rootCause"]="", ["workaround"]="Old workaround", ["resolution"]="" };
var after = new Dictionary<string, object?> { ["title"]="Same title", ["description"]="New description", ["status"]="INVESTIGATING", ["ownerUserId"]="new-owner", ["rootCause"]="Confirmed cause", ["workaround"]="New workaround", ["resolution"]="Verified fix" };
var details = ProblemAuditDetails.Changes("PRB-TEST", before, after);
var oldFields=(Dictionary<string, object?>)details["before"]!;
var newFields=(Dictionary<string, object?>)details["after"]!;
Check(oldFields.Count == 6 && newFields.Count == 6 && !newFields.ContainsKey("title"), "Audit must include only changed fields");
Check(Equals(oldFields["ownerUserId"],"old-owner") && Equals(newFields["ownerUserId"],"new-owner"), "Audit lost previous/new owner");
Check(Equals(newFields["resolution"],"Verified fix") && Equals(details["problemNumber"],"PRB-TEST"), "Audit lost resolution or reference");
Check(((Dictionary<string,object?>)ProblemAuditDetails.Changes("PRB-TEST",before,before)["after"]!).Count == 0, "Unchanged fields must not appear as changes");
Console.WriteLine("PASS: detailed before/after audit, ownership, unchanged fields and reference.");
var builder = WebApplication.CreateBuilder();
builder.Logging.ClearProviders();
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options => {
    AuthSessionPolicy.Configure(options,lifetime);
    options.TimeProvider=clock;
    options.Events.OnRedirectToLogin = context => { context.Response.StatusCode=401; return Task.CompletedTask; };
});
builder.Services.AddAuthorization();
await using var app=builder.Build();
app.UseAuthentication();app.UseAuthorization();
app.MapGet("/login", async (HttpContext context) => { await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity([new System.Security.Claims.Claim("name","test")],CookieAuthenticationDefaults.AuthenticationScheme)),AuthSessionPolicy.SignIn(lifetime,clock.GetUtcNow()));return Results.Ok(); });
app.MapGet("/protected",()=>Results.Ok()).RequireAuthorization();
await app.StartAsync();
try {
    var address=app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
    using var client=new HttpClient(new HttpClientHandler {CookieContainer=new CookieContainer(),AllowAutoRedirect=false}) {BaseAddress=new Uri(address)};
    Check((await client.GetAsync("/login")).IsSuccessStatusCode,"Test sign-in failed");
    clock.Advance(TimeSpan.FromMinutes(70));
    Check((await client.GetAsync("/protected")).IsSuccessStatusCode,"Session expired at the old one-hour boundary");
    clock.Advance(TimeSpan.FromHours(3));
    var renewal=await client.GetAsync("/protected");
    Check(renewal.IsSuccessStatusCode && renewal.Headers.Contains("Set-Cookie"),"Active session did not renew");
    clock.Advance(TimeSpan.FromHours(5));
    Check((await client.GetAsync("/protected")).IsSuccessStatusCode,"Renewed session expired too early");
    clock.Advance(TimeSpan.FromHours(9));
    Check((await client.GetAsync("/protected")).StatusCode==HttpStatusCode.Unauthorized,"Inactive session did not expire");
    Console.WriteLine("PASS: real cookie survives 70 minutes, renews with activity, and expires after inactivity.");
} finally { await app.StopAsync(); }
sealed class ManualClock : TimeProvider {
    private DateTimeOffset current=DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    public override DateTimeOffset GetUtcNow()=>current;
    public void Advance(TimeSpan duration)=>current=current.Add(duration);
}
'@ | Set-Content -LiteralPath (Join-Path $scratch 'Program.cs') -Encoding utf8
  '<configuration><packageSources><clear /></packageSources></configuration>' | Set-Content -LiteralPath (Join-Path $scratch 'NuGet.Config') -Encoding utf8
  dotnet restore (Join-Path $scratch 'Checks.csproj') --configfile (Join-Path $scratch 'NuGet.Config')
  if ($LASTEXITCODE -ne 0) { throw 'Isolated test restore failed.' }
  dotnet run --no-restore --project (Join-Path $scratch 'Checks.csproj')
  if ($LASTEXITCODE -ne 0) { throw 'Policy verification failed.' }
} finally {
  $tempRoot=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
  $resolved=[IO.Path]::GetFullPath($scratch)
  if ($resolved.StartsWith($tempRoot,[StringComparison]::OrdinalIgnoreCase) -and [IO.Path]::GetFileName($resolved).StartsWith('astra-policy-tests-')) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
