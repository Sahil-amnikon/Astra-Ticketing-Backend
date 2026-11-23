$ErrorActionPreference = 'Stop'
$backend = Split-Path $PSScriptRoot -Parent
$scratch = Join-Path $PSScriptRoot ('.proxy-https-check-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($scratch) | Out-Null
try {
  $api = [Security.SecurityElement]::Escape((Join-Path $backend 'bin/Debug/net10.0/Astra.Api.dll'))
  $mysql = [Security.SecurityElement]::Escape((Join-Path $backend 'bin/Debug/net10.0/MySqlConnector.dll'))
  "<Project Sdk=`"Microsoft.NET.Sdk`"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup><ItemGroup><FrameworkReference Include=`"Microsoft.AspNetCore.App`"/><Reference Include=`"Astra.Api`"><HintPath>$api</HintPath></Reference><Reference Include=`"MySqlConnector`"><HintPath>$mysql</HintPath></Reference></ItemGroup></Project>" | Set-Content -LiteralPath (Join-Path $scratch 'Check.csproj')
  '<configuration><packageSources><clear /></packageSources></configuration>' | Set-Content -LiteralPath (Join-Path $scratch 'NuGet.Config')
  @'
using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
static void Check(bool value,string message){if(!value)throw new Exception(message);}
using var logs=LoggerFactory.Create(_=>{});
foreach(var (remote,expected) in new[]{(IPAddress.Loopback,"https"),(IPAddress.IPv6Loopback,"https"),(IPAddress.Loopback.MapToIPv6(),"https"),(IPAddress.Parse("198.51.100.5"),"http")}){
 var context=new DefaultHttpContext();context.Connection.RemoteIpAddress=remote;context.Request.Scheme="http";context.Request.Host=new HostString("astra.amnikontechnologies.com");context.Request.Headers["X-Forwarded-Proto"]="https";context.Request.Headers["X-Forwarded-Host"]="attacker.example";
 var middleware=new ForwardedHeadersMiddleware(_=>Task.CompletedTask,logs,Options.Create(TrustedProxyHttps.Options()));await middleware.Invoke(context);
 Check(context.Request.Scheme==expected,"Unexpected scheme from "+remote);
 Check(context.Request.Host.Host=="astra.amnikontechnologies.com","Forwarded host must not be trusted");
}
var options=TrustedProxyHttps.Options();Check(options.ForwardLimit==1 && options.KnownIPNetworks.Count==0 && options.KnownProxies.Count==3,"Only explicit loopback proxies may be trusted");
Check(TrustedProxyHttps.CallbackUrl("https://astra.amnikontechnologies.com")=="https://astra.amnikontechnologies.com/api/auth/callback","Public callback must use HTTPS");
Check(TrustedProxyHttps.CallbackUrl("http://localhost:3201")=="http://localhost:3201/api/auth/callback","Local development stays supported");
foreach(var input in new[]{"http://astra.amnikontechnologies.com","https://user:password@example.test","https://example.test/path","https://example.test/?next=evil","https://example.test/#fragment","not a URL"}){
 try{TrustedProxyHttps.CallbackUrl(input);throw new Exception("Invalid public origin accepted");}catch(ArgumentException){}
}
var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["ASTRA_PUBLIC_ORIGIN"]="https://astra.amnikontechnologies.com",["APP_BASE_URL"]="http://old.example"}).Build();
Check(AuthSettings.Load(config).ApplicationBaseUrl=="https://astra.amnikontechnologies.com","Explicit public origin must take priority");
Console.WriteLine("PASS: local proxy HTTPS handling, rejection of untrusted proxies, unchanged Host, canonical callback and public-origin priority. No network or database accessed.");
'@ | Set-Content -LiteralPath (Join-Path $scratch 'Program.cs')
  & dotnet build (Join-Path $scratch 'Check.csproj') --configfile (Join-Path $scratch 'NuGet.Config') -p:NuGetAudit=false --nologo -v:q
  if ($LASTEXITCODE -ne 0) { throw 'Proxy HTTPS test build failed.' }
  & dotnet (Join-Path $scratch 'bin/Debug/net10.0/Check.dll')
  if ($LASTEXITCODE -ne 0) { throw 'Proxy HTTPS tests failed.' }
} finally {
  $resolved = [IO.Path]::GetFullPath($scratch)
  $allowed = [IO.Path]::GetFullPath($PSScriptRoot) + [IO.Path]::DirectorySeparatorChar
  if (!$resolved.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase) -or !(Split-Path $resolved -Leaf).StartsWith('.proxy-https-check-')) { throw 'Unexpected test directory.' }
  Remove-Item -LiteralPath $resolved -Recurse -Force
}
