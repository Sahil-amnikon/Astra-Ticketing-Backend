using System.Reflection;
using System.Text.Json;
var type=typeof(MySqlStore);
var flags=BindingFlags.Static|BindingFlags.NonPublic;
var map=type.GetMethod("StoredReviewNotificationType",flags)!;
string ConvertType(string value)=>(string)map.Invoke(null,[value])!;
if(ConvertType("SUBMISSION_REVIEW")!="CHANGE_APPROVAL_REQUESTED" || ConvertType("SUBMISSION_DECIDED")!="CHANGE_STATUS_UPDATED" || ConvertType("CHANGE_TASK_ASSIGNED")!="CHANGE_TASK_ASSIGNED")
    throw new Exception("Review notification vocabulary compatibility failed.");
var sql=(string)type.GetField("ReviewNotificationInsertSql",flags)!.GetRawConstantValue()!;
if(args.Length==1)await File.WriteAllTextAsync(args[0],JsonSerializer.Serialize(new{sql,request=ConvertType("SUBMISSION_REVIEW"),decision=ConvertType("SUBMISSION_DECIDED")}));
Console.WriteLine("PASS: actual review alert mapping uses installed approval/status types and retains unrelated notification types. No application database accessed.");
