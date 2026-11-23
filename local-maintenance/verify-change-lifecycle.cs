using System.Reflection;
using System.Text.Json;
object? Call(string name,params object?[] values)=>typeof(MySqlStore).GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,values);
string[] Next(string state,string review,bool owner,bool reviewer)=>(string[])Call("ReviewedChangeNextStatuses",state,review,owner,reviewer)!;
void Expect(string state,string review,bool owner,bool reviewer,params string[] expected){if(!Next(state,review,owner,reviewer).SequenceEqual(expected))throw new Exception($"Unexpected actions for {state}/{review}, owner={owner}, reviewer={reviewer}");}
foreach(var state in new[]{"DRAFT","SUBMITTED","APPROVED","IN_PROGRESS","COMPLETED"})foreach(var review in new[]{"PENDING","REJECTED"})Expect(state,review,true,true);
foreach(var state in new[]{"DRAFT","SUBMITTED","ASSESSMENT","AWAITING_APPROVAL","APPROVED","SCHEDULED"}){
 Expect(state,"APPROVED",true,false,"IN_PROGRESS");Expect(state,"APPROVED",false,true);Expect(state,"APPROVED",false,false);
}
Expect("IN_PROGRESS","APPROVED",true,false,"COMPLETED","FAILED","ROLLED_BACK");
foreach(var state in new[]{"COMPLETED","FAILED","ROLLED_BACK"}){
 Expect(state,"APPROVED",false,true,"CLOSED");Expect(state,"APPROVED",true,false);Expect(state,"APPROVED",true,true);Expect(state,"APPROVED",false,false);
}
Expect("CLOSED","APPROVED",true,true);Expect("CANCELLED","APPROVED",true,true);
string Constant(string name)=>(string)typeof(MySqlStore).GetField(name,BindingFlags.Static|BindingFlags.NonPublic)!.GetRawConstantValue()!;
string Property(string name)=>(string)typeof(MySqlStore).GetProperty(name,BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
await File.WriteAllTextAsync(args[0],JsonSerializer.Serialize(new{workflow=Property("ReviewedChangeWorkflowSql"),submitted=Constant("MarkReviewedChangeSubmittedSql"),approved=Constant("ApprovedReviewedChangeSql")}));
Console.WriteLine("PASS: pending/rejected changes cannot start; only the owner implements; completed work requires independent closure; approved templates cannot bypass change review; closed records are terminal.");
