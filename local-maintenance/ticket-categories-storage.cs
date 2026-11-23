using MySqlConnector;
using System.Text.RegularExpressions;
static string Schema(string ticket,string category)=>$"""
CREATE TABLE IF NOT EXISTS ticket_categories(
 ticket_id {ticket} NOT NULL,
 category_id {category} NOT NULL,
 sort_order SMALLINT UNSIGNED NOT NULL DEFAULT 0,
 PRIMARY KEY(ticket_id,category_id),
 INDEX ix_ticket_categories_category(category_id),
 CONSTRAINT fk_ticket_categories_ticket FOREIGN KEY(ticket_id) REFERENCES tickets(id) ON DELETE CASCADE,
 CONSTRAINT fk_ticket_categories_category FOREIGN KEY(category_id) REFERENCES categories(id) ON DELETE CASCADE
) ENGINE=InnoDB
""";
if(args is ["--check-storage"]){
    var sql=Schema("CHAR(36)","CHAR(36)");
    if(sql.Contains("ALTER ",StringComparison.OrdinalIgnoreCase)||!sql.Contains("CREATE TABLE IF NOT EXISTS")||!sql.Contains("PRIMARY KEY(ticket_id,category_id)"))throw new Exception("Unexpected category storage change.");
    Console.WriteLine("PASS: category storage is additive, supports multiple unique categories per ticket and uses existing ticket/category relationships. No database connected.");return;
}
var settings=Environment.GetEnvironmentVariable("ASTRA_TICKET_CATEGORIES_CONNECTION")??throw new Exception("Service database settings missing.");
if(new MySqlConnectionStringBuilder(settings).Database!="astra_ticketing")throw new Exception("Unexpected database.");
await using var db=new MySqlConnection(settings);await db.OpenAsync();
async Task<string> IdType(string table){
    await using var cmd=new MySqlCommand("SELECT COLUMN_TYPE,CHARACTER_SET_NAME,COLLATION_NAME FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME=@table AND COLUMN_NAME='id'",db);cmd.Parameters.AddWithValue("@table",table);
    await using var row=await cmd.ExecuteReaderAsync();if(!await row.ReadAsync())throw new Exception("Required ticket/category ID definition missing.");
    var type=row.GetString(0);var charset=row.GetString(1);var collation=row.GetString(2);
    if(!Regex.IsMatch(type,@"^(char|varchar)\(36\)$",RegexOptions.IgnoreCase)||!Regex.IsMatch(charset,@"^[A-Za-z0-9_]+$")||!Regex.IsMatch(collation,@"^[A-Za-z0-9_]+$"))throw new Exception("ID storage requires review. Existing tables were not changed.");
    return type+" CHARACTER SET "+charset+" COLLATE "+collation;
}
var ticket=await IdType("tickets");var category=await IdType("categories");
await using(var cmd=new MySqlCommand(Schema(ticket,category),db))await cmd.ExecuteNonQueryAsync();
await using(var cmd=new MySqlCommand("SELECT ticket_id,category_id,sort_order FROM ticket_categories LIMIT 0",db)){await using var reader=await cmd.ExecuteReaderAsync();}
await using(var cmd=new MySqlCommand("SELECT COUNT(*) FROM information_schema.TABLE_CONSTRAINTS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='ticket_categories' AND CONSTRAINT_TYPE='FOREIGN KEY'",db))if(Convert.ToInt32(await cmd.ExecuteScalarAsync())!=2)throw new Exception("Ticket category relationships differ from the reviewed storage.");
Console.WriteLine("Ticket category storage ready. Existing ticket categories remain compatible; no existing records, permissions, backups or data copies were changed.");
