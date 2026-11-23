// Guarded HTTPS sign-in source update; no account or database operations.
import fs from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
const patches=[{"path":"Program.cs","before":"    options.Events.OnTokenValidated = async context =>","after":"    options.Events.OnRedirectToIdentityProvider = context =>\n    {\n        context.ProtocolMessage.RedirectUri = TrustedProxyHttps.CallbackUrl(settings.ApplicationBaseUrl);\n        return Task.CompletedTask;\n    };\n    options.Events.OnTokenValidated = async context =>"},{"path":"Program.cs","before":"app.UseExceptionHandler();","after":"app.UseExceptionHandler();\napp.UseForwardedHeaders(TrustedProxyHttps.Options());"},{"path":"Program.cs","before":"config[\"APP_BASE_URL\"]","after":"config[\"ASTRA_PUBLIC_ORIGIN\"] ?? config[\"APP_BASE_URL\"]"},{"path":"Database/FeatureSchemaStartup.cs","before":"public sealed class FeatureSchemaStartup(MySqlStore store, IConfiguration config) : IHostedService","after":"public sealed class FeatureSchemaStartup(MySqlStore store, IConfiguration config, IHostEnvironment environment) : IHostedService"},{"path":"Database/FeatureSchemaStartup.cs","before":"        if (config.GetValue<bool>(\"ASTRA_SKIP_FEATURE_SCHEMA_STARTUP\") ||","after":"        if (environment.IsProduction() ||\n            config.GetValue<bool>(\"ASTRA_SKIP_FEATURE_SCHEMA_STARTUP\") ||"}];
const helper="using System.Net;\nusing Microsoft.AspNetCore.HttpOverrides;\n\npublic static class TrustedProxyHttps\n{\n    public static ForwardedHeadersOptions Options()\n    {\n        var options = new ForwardedHeadersOptions\n        {\n            ForwardedHeaders = ForwardedHeaders.XForwardedProto,\n            ForwardLimit = 1\n        };\n        options.KnownIPNetworks.Clear();\n        options.KnownProxies.Clear();\n        options.KnownProxies.Add(IPAddress.Loopback);\n        options.KnownProxies.Add(IPAddress.IPv6Loopback);\n        options.KnownProxies.Add(IPAddress.Loopback.MapToIPv6());\n        return options;\n    }\n\n    public static string CallbackUrl(string applicationBaseUrl)\n    {\n        if (!Uri.TryCreate(applicationBaseUrl, UriKind.Absolute, out var origin) ||\n            !string.IsNullOrEmpty(origin.UserInfo) || !string.IsNullOrEmpty(origin.Query) ||\n            !string.IsNullOrEmpty(origin.Fragment) || origin.AbsolutePath != \"/\" ||\n            (origin.Scheme != Uri.UriSchemeHttps && !(origin.Scheme == Uri.UriSchemeHttp && origin.IsLoopback)))\n            throw new ArgumentException(\"Configure ASTRA's public address as an HTTPS origin.\");\n        return new Uri(origin, \"/api/auth/callback\").AbsoluteUri;\n    }\n}\n";
if(!['--check','--apply'].includes(process.argv[2]))throw Error('Use --check or --apply.');
const apply=process.argv[2]==='--apply';
const backend=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
const norm=s=>s.replace(/\r\n/g,'\n').trimEnd()+'\n';
const read=async file=>norm(await fs.readFile(file,'utf8'));
const changes=new Map();
const program=await read(path.join(backend,'Program.cs'));
if(program.includes('app.UseForwardedHeaders(')&&!program.includes('app.UseForwardedHeaders(TrustedProxyHttps.Options());'))throw Error('STOP: existing proxy handling differs. No files changed.');
if(program.includes('options.Events.OnRedirectToIdentityProvider =')&&!program.includes('TrustedProxyHttps.CallbackUrl(settings.ApplicationBaseUrl)'))throw Error('STOP: existing redirect handler differs. No files changed.');
for(const patch of patches){
 const file=path.join(backend,patch.path);let source=changes.get(file)??await read(file);
 if(!source.includes(patch.after)){
 if(source.split(patch.before).length!==2)throw Error('STOP: HTTPS source differs in '+patch.path+'. No files changed.');
 source=source.replace(patch.before,patch.after);
 }
 changes.set(file,source);
}
const file=path.join(backend,'TrustedProxyHttps.cs');
let existing;try{existing=await read(file)}catch(e){if(e.code!=='ENOENT')throw e}
if(existing!==undefined&&existing!==norm(helper))throw Error('STOP: HTTPS helper differs. No files changed.');
changes.set(file,norm(helper));
for(const [file,next] of changes){
 let current;try{current=await read(file)}catch(e){if(e.code!=='ENOENT')throw e}
 if(current!==next){if(apply)await fs.writeFile(file,next);console.log((apply?'Updated: ':'Would update: ')+path.relative(backend,file))}
}
console.log('HTTPS source preflight passed. No database or account changes.');

