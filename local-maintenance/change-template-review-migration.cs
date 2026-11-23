using System.Text.RegularExpressions;
using MySqlConnector;

static string TemplateReviewSchema(string userId, string templateId) => $@"
CREATE TABLE IF NOT EXISTS change_template_reviews (
 kind VARCHAR(20) NOT NULL DEFAULT 'CHANGE_TEMPLATE',
 entity_id {templateId} NOT NULL,
 submitted_by_user_id {userId} NOT NULL,
 status VARCHAR(16) NOT NULL DEFAULT 'PENDING',
 revision INT NOT NULL DEFAULT 1,
 submitted_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
 reviewed_by_user_id {userId} NULL,
 reviewed_at DATETIME(6) NULL,
 comment VARCHAR(2000) NULL,
 PRIMARY KEY (kind,entity_id),
 KEY ix_template_review_queue (status,submitted_at),
 KEY ix_template_review_author (submitted_by_user_id),
 CONSTRAINT fk_template_review_entity FOREIGN KEY (entity_id) REFERENCES change_templates(id) ON DELETE CASCADE,
 CONSTRAINT fk_template_review_author FOREIGN KEY (submitted_by_user_id) REFERENCES users(id) ON DELETE CASCADE,
 CONSTRAINT fk_template_review_reviewer FOREIGN KEY (reviewed_by_user_id) REFERENCES users(id) ON DELETE SET NULL,
 CONSTRAINT ck_template_review_kind CHECK (kind='CHANGE_TEMPLATE'),
 CONSTRAINT ck_template_review_status CHECK (status IN ('PENDING','APPROVED','REJECTED')),
 CONSTRAINT ck_template_review_revision CHECK (revision>0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci";

if (args.Length >= 1 && args[0] == "--check-template-storage")
{
    var schema = TemplateReviewSchema("CHAR(36)", "CHAR(36)");
    if (Regex.IsMatch(schema, @"\b(?:ALTER|DROP|TRUNCATE|UPDATE|INSERT|DELETE\s+FROM)\b", RegexOptions.IgnoreCase))
        throw new Exception("Unexpected existing-data mutation in template table creation.");
    if (args.Length == 2) await File.WriteAllTextAsync(args[1], schema);
    Console.WriteLine("PASS: template review storage requires only creation of its own table; no ALTER, privilege grants, existing-data updates or copies. No database connection opened.");
    return;
}

static async Task<string> IdDefinition(MySqlConnection connection, string table)
{
    if (table is not ("users" or "change_templates")) throw new Exception("Unexpected schema target.");
    await using var command = new MySqlCommand("SELECT COLUMN_TYPE,CHARACTER_SET_NAME,COLLATION_NAME FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME=@table AND COLUMN_NAME='id'", connection);
    command.Parameters.AddWithValue("@table", table);
    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync()) throw new Exception("Required ID definition missing.");
    var type = reader.GetString(0); var charset = reader.GetString(1); var collation = reader.GetString(2);
    if (!Regex.IsMatch(type, @"^(?:char|varchar)\(36\)$", RegexOptions.IgnoreCase) || !Regex.IsMatch(charset, @"^[a-zA-Z0-9_]+$") || !Regex.IsMatch(collation, @"^[a-zA-Z0-9_]+$"))
        throw new Exception("Unexpected ID definition. Nothing changed.");
    return type + " CHARACTER SET " + charset + " COLLATE " + collation;
}

var settings = Environment.GetEnvironmentVariable("ASTRA_TEMPLATE_REVIEW_CONNECTION") ?? throw new Exception("Database settings missing.");
if (new MySqlConnectionStringBuilder(settings).Database != "astra_ticketing") throw new Exception("Unexpected database.");
await using var connection = new MySqlConnection(settings); await connection.OpenAsync();
await using (var previous = new MySqlCommand("SELECT COUNT(*) FROM submission_reviews WHERE kind='CHANGE_TEMPLATE'", connection))
    if (Convert.ToInt32(await previous.ExecuteScalarAsync()) != 0)
        throw new Exception("Existing template reviews require a separate migration review. No records copied or changed.");
var sql = TemplateReviewSchema(await IdDefinition(connection, "users"), await IdDefinition(connection, "change_templates"));
await using (var create = new MySqlCommand(sql, connection)) await create.ExecuteNonQueryAsync();
await using (var shape = new MySqlCommand("SELECT kind,entity_id,submitted_by_user_id,status,revision,submitted_at,reviewed_by_user_id,reviewed_at,comment FROM change_template_reviews LIMIT 0", connection))
{ await using var reader = await shape.ExecuteReaderAsync(); }
await using (var keys = new MySqlCommand("SELECT COUNT(*) FROM information_schema.TABLE_CONSTRAINTS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='change_template_reviews' AND CONSTRAINT_TYPE='FOREIGN KEY'", connection))
    if (Convert.ToInt32(await keys.ExecuteScalarAsync()) != 3) throw new Exception("Template review relationships require review. No existing records changed.");
Console.WriteLine("Change template review table ready. Existing tables, records and database permissions were not altered. No backups.");
