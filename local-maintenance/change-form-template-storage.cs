using System.Text.RegularExpressions;
using MySqlConnector;

static string ChangeFormTemplateSchema(string userId) => $@"
CREATE TABLE IF NOT EXISTS change_form_templates (
 id CHAR(36) NOT NULL PRIMARY KEY,
 name VARCHAR(200) NOT NULL,
 plan JSON NOT NULL,
 status VARCHAR(16) NOT NULL,
 approval_required TINYINT(1) NOT NULL,
 revision INT NOT NULL DEFAULT 1,
 created_by_user_id {userId} NOT NULL,
 created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
 reviewed_by_user_id {userId} NULL,
 reviewed_at DATETIME(6) NULL,
 review_comment VARCHAR(2000) NULL,
 published_at DATETIME(6) NULL,
 KEY ix_change_plan_status (status,created_at),
 KEY ix_change_plan_author (created_by_user_id),
 CONSTRAINT fk_change_plan_author FOREIGN KEY (created_by_user_id) REFERENCES users(id) ON DELETE CASCADE,
 CONSTRAINT fk_change_plan_reviewer FOREIGN KEY (reviewed_by_user_id) REFERENCES users(id) ON DELETE SET NULL,
 CONSTRAINT ck_change_plan_status CHECK (status IN ('PENDING','APPROVED','REJECTED')),
 CONSTRAINT ck_change_plan_revision CHECK (revision>0),
 CONSTRAINT ck_change_plan_approval CHECK (approval_required IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci";

if(args.Length>=1&&args[0]=="--check-template-storage"){
 var schema=ChangeFormTemplateSchema("CHAR(36)");
 if(Regex.IsMatch(schema,@"\b(?:ALTER|DROP|TRUNCATE|UPDATE|INSERT|GRANT|REVOKE)\b",RegexOptions.IgnoreCase))throw new Exception("Unexpected existing-data change in storage creation.");
 if(args.Length==2)await File.WriteAllTextAsync(args[1],schema);
 Console.WriteLine("PASS: full change-template storage is CREATE-only, with no ALTER or permission grants. No database connection opened.");
 return;
}
var settings=Environment.GetEnvironmentVariable("ASTRA_FORM_TEMPLATE_CONNECTION")??throw new Exception("Database settings missing.");
if(new MySqlConnectionStringBuilder(settings).Database!="astra_ticketing")throw new Exception("Unexpected database.");
await using var connection=new MySqlConnection(settings);await connection.OpenAsync();
await using var query=new MySqlCommand("SELECT COLUMN_TYPE,CHARACTER_SET_NAME,COLLATION_NAME FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='users' AND COLUMN_NAME='id'",connection);
await using var reader=await query.ExecuteReaderAsync();
if(!await reader.ReadAsync())throw new Exception("User ID definition missing.");
var type=reader.GetString(0);var charset=reader.GetString(1);var collation=reader.GetString(2);
if(!Regex.IsMatch(type,@"^(?:char|varchar)\(36\)$",RegexOptions.IgnoreCase)||!Regex.IsMatch(charset,@"^[a-zA-Z0-9_]+$")||!Regex.IsMatch(collation,@"^[a-zA-Z0-9_]+$"))throw new Exception("Unexpected user ID definition. Nothing changed.");
await reader.DisposeAsync();
await using(var create=new MySqlCommand(ChangeFormTemplateSchema(type+" CHARACTER SET "+charset+" COLLATE "+collation),connection))await create.ExecuteNonQueryAsync();
await using(var shape=new MySqlCommand("SELECT id,name,plan,status,approval_required,revision,created_by_user_id,created_at,reviewed_by_user_id,reviewed_at,review_comment,published_at FROM change_form_templates LIMIT 0",connection)){await using var check=await shape.ExecuteReaderAsync();}
await using(var keys=new MySqlCommand("SELECT COUNT(*) FROM information_schema.TABLE_CONSTRAINTS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='change_form_templates' AND CONSTRAINT_TYPE='FOREIGN KEY'",connection))if(Convert.ToInt32(await keys.ExecuteScalarAsync())!=2)throw new Exception("Change template relationships require review.");
Console.WriteLine("Full change-template storage ready. Existing tables, templates, tickets and database permissions retained. No backups.");
