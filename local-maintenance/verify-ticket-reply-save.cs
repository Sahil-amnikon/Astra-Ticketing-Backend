using System.Reflection;
var assembly=typeof(MySqlStore).Assembly;
var validate=assembly.GetType("PublicTicketReplyPolicy")!.GetMethod("Validate",BindingFlags.NonPublic|BindingFlags.Static)!;
foreach(var reply in new[]{"Please restart the workstation and try again.","The replacement device is ready for collection.","I have escalated this to the network team.","Test reply","Please use the official password reset portal."})
    validate.Invoke(null,[reply,new[]{"Outlook fails to open","The email application does not start."}]);
foreach(var reply in new[]{"Please send your password.","Share your verification code.","Please give me your API key."})
{
    try {validate.Invoke(null,[reply,Array.Empty<string>()]);throw new Exception("Credential request was accepted.");}
    catch(TargetInvocationException e) when(e.InnerException is FeatureFailure failure && failure.Code=="PUBLIC_REPLY_CREDENTIAL_REQUEST") {}
}
var convert=typeof(MySqlStore).GetMethod("TicketNotificationRecipientId",BindingFlags.NonPublic|BindingFlags.Static)!;
var id=Guid.NewGuid();
if((string)convert.Invoke(null,[id])! != id.ToString("D") || (string)convert.Invoke(null,[id.ToString("D")])! != id.ToString("D"))
    throw new Exception("Native GUID and string IDs must produce the same notification recipient.");
try {convert.Invoke(null,["invalid"]);throw new Exception("Invalid recipient was accepted.");}
catch(TargetInvocationException e) when(e.InnerException is InvalidOperationException) {}
Console.WriteLine("PASS: normal public replies pass validation without word-overlap rejection; credential-request protection remains; native GUID notification recipients convert correctly. No application database, email or network accessed.");
