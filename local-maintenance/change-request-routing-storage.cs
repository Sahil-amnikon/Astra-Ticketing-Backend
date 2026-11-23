using System.Text.RegularExpressions;
using MySqlConnector;

static string Schema(string changeId,string userId)=>$@"
CREATE TABLE IF NOT EXISTS change_request_details (
 change_id {changeId} NOT NULL PRIMARY KEY,
 site_ids JSON NOT NULL,
 department_ids JSON NOT NULL,
 requester_kind VARCHAR(10) NOT NULL,
 requester_id {userId} NOT NULL,
 approver_user_id {userId} NULL,
 CONSTRAINT fk_request_details_change FOREIGN KEY(change_id) REFERENCES changes(id) ON DELETE CASCADE,
 CONSTRAINT fk_request_details_approver FOREIGN KEY(approver_user_id) REFERENCES users(id) ON DELETE SET NULL,
 CONSTRAINT ck_request_details_kind CHECK(requester_kind IN ('USER','CONTACT')),
 CONSTRAINT ck_request_details_sites CHECK(JSON_TYPE(site_ids)='ARRAY' AND JSON_LENGTH(site_ids)<=50),
 CONSTRAINT ck_request_details_departments CHECK(JSON_TYPE(department_ids)='ARRAY' AND JSON_LENGTH(department_ids)<=200)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci";
if(args.Length>0&&args[0]=="--check-routing-storage"){
 var sql=Schema("CHAR(36)","CHAR(36)");
 if(Regex.IsMatch(sql,@"\b(ALTER|DROP|TRUNCATE|UPDATE|INSERT|GRANT|REVOKE)\b",RegexOptions.IgnoreCase))throw new Exception("Unexpected database mutation.");
 if(args.Length==2)await File.WriteAllTextAsync(args[1],sql);
 Console.WriteLine("PASS: request routing storage is CREATE-only. No database connection opened.");return;
}
var settings=Environment.GetEnvironmentVariable("ASTRA_CHANGE_ROUTING_CONNECTION")??throw new Exception("Database settings missing.");
if(new MySqlConnectionStringBuilder(settings).Database!="astra_ticketing")throw new Exception("Unexpected database.");
await using var c=new MySqlConnection(settings);await c.OpenAsync();
async Task<string> Id(string table){
 await using var cmd=new MySqlCommand("SELECT COLUMN_TYPE,CHARACTER_SET_NAME,COLLATION_NAME FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME=@table AND COLUMN_NAME='id'",c);cmd.Parameters.AddWithValue("@table",table);
 await using var row=await cmd.ExecuteReaderAsync();if(!await row.ReadAsync())throw new Exception("Required ID definition missing.");
 var type=row.GetString(0);var charset=row.GetString(1);var collation=row.GetString(2);
 if(!Regex.IsMatch(type,@"^(char|varchar)\(36\)$",RegexOptions.IgnoreCase)||!Regex.IsMatch(charset,@"^[A-Za-z0-9_]+$")||!Regex.IsMatch(collation,@"^[A-Za-z0-9_]+$"))throw new Exception("ID definition differs. Nothing changed.");
 return type+" CHARACTER SET "+charset+" COLLATE "+collation;
}
var change=await Id("changes");var user=await Id("users");
await using(var create=new MySqlCommand(Schema(change,user),c))await create.ExecuteNonQueryAsync();
await using(var shape=new MySqlCommand("SELECT change_id,site_ids,department_ids,requester_kind,requester_id,approver_user_id FROM change_request_details LIMIT 0",c)){await using var row=await shape.ExecuteReaderAsync();}
Console.WriteLine("Change request routing storage ready. Existing tables, records and database permissions retained. No backups.");
