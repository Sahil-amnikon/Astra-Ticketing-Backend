using System.Reflection;
using System.Text.Json;
var flags=BindingFlags.Static|BindingFlags.NonPublic;var type=typeof(MySqlStore);
var normalize=type.GetMethod("NormalizeTicketCategories",flags)!;
string[] Normalize(string primary,string[]? ids)=>(string[])normalize.Invoke(null,[primary,ids])!;
var first=Guid.NewGuid().ToString("D");var second=Guid.NewGuid().ToString("D");
if(!Normalize(first,null).SequenceEqual(new[]{first}))throw new Exception("Legacy single-category requests must remain valid.");
if(!Normalize(first,[first.ToUpperInvariant(),second,second.ToUpperInvariant()]).SequenceEqual(new[]{first,second}))throw new Exception("Primary order or duplicate normalization failed.");
foreach(var invalid in new[]{new[]{"invalid"},Enumerable.Range(0,501).Select(_=>Guid.NewGuid().ToString("D")).ToArray()}){
    try{Normalize(first,invalid);throw new Exception("Invalid category selection accepted.");}
    catch(TargetInvocationException e)when(e.InnerException is ArgumentException){}
}
if(typeof(CreateTicketRequest).GetProperty("CategoryIds") is null)throw new Exception("Ticket category array is missing from the API request.");
var sql=(string)type.GetField("TicketCategoryInsertSql",flags)!.GetRawConstantValue()!;
var check=(string)type.GetMethod("TicketCategoryCheckSql",flags)!.Invoke(null,[new[]{"@category0","@category1"}])!;
if(args.Length==1)await File.WriteAllTextAsync(args[0],JsonSerializer.Serialize(new{sql,check}));
Console.WriteLine("PASS: actual ticket category normalization supports legacy requests, primary order, duplicate/case handling and validation limits. No database accessed.");
