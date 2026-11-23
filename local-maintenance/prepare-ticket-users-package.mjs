import fs from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
const folder=path.dirname(fileURLToPath(import.meta.url));
const root=path.resolve(folder,'../..');
const read=async name=>(await fs.readFile(path.join(folder,name),'utf8')).replace(/\r\n/g,'\n');
const files=[];
for(const name of ['ASTRA-Backend-latest/FeatureMultipleTicketUsers.cs','ASTRA-Frontend-latest/src/pages/ticket-assignee-default.ts'])files.push({path:name,content:(await fs.readFile(path.join(root,name),'utf8')).replace(/\r\n/g,'\n')});
let updater=await read('apply-multiple-ticket-users.mjs');
if(!updater.includes('const files='))updater=updater.replace('const changes=new Map();','const files='+JSON.stringify(files)+';\nconst changes=new Map();');
else updater=updater.replace(/^const files=.*;$/m,()=> 'const files='+JSON.stringify(files)+';');
if(!updater.includes('for(const item of files)'))updater=updater.replace("if(!['--check','--apply'].includes(process.argv[2]))",`for(const item of files){
 const file=path.join(root,item.path);let existing=null;
 try{existing=(await fs.readFile(file,'utf8')).replace(/\\r\\n/g,'\\n')}catch(e){if(e.code!=='ENOENT')throw e}
 if(existing!==null&&existing!==item.content)throw Error('STOP: ticket-user helper differs. No files changed.');
 changes.set(file,item.content);
}
if(!['--check','--apply'].includes(process.argv[2]))`);
await fs.writeFile(path.join(folder,'apply-multiple-ticket-users.mjs'),updater);
let migration=await read('migrate-custom-roles.py');
migration=migration.replaceAll('migrate-custom-roles.py','migrate-multiple-ticket-users.py').replaceAll('custom-roles.sql','multiple-ticket-users.sql')
 .replaceAll('ASTRA_CUSTOM_ROLE_CONNECTION','ASTRA_TICKET_USERS_CONNECTION').replaceAll('ASTRA_CUSTOM_ROLE_SCHEMA','ASTRA_TICKET_USERS_SCHEMA')
 .replaceAll('new[]{"custom_role_profiles","user_custom_roles"}','new[]{"ticket_additional_users"}')
 .replaceAll("TABLE_NAME='user_custom_roles'","TABLE_NAME='ticket_additional_users'")
 .replaceAll('Custom role storage','Additional ticket-user storage').replaceAll('Custom role relationship','Additional ticket-user relationship')
 .replaceAll('.custom-role-migration-code-','.ticket-user-migration-code-')
 .replace('Create storage for administrator-defined roles only.','Create storage for additional users on a ticket.');
const marker="foreach(var statement in schema.Split";
const definitions=`async Task<string> Definition(string table){
 await using var query=new MySqlCommand("SELECT COLUMN_TYPE,CHARACTER_SET_NAME,COLLATION_NAME FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME=@table AND COLUMN_NAME='id'",connection);query.Parameters.AddWithValue("@table",table);
 await using var row=await query.ExecuteReaderAsync();if(!await row.ReadAsync())throw new Exception("Required ID definition missing.");
 var type=row.GetString(0);var charset=row.GetString(1);var collation=row.GetString(2);
 if(!Regex.IsMatch(type,@"^(?:char|varchar)\\(36\\)$",RegexOptions.IgnoreCase)||!Regex.IsMatch(charset,@"^[a-zA-Z0-9_]+$")||!Regex.IsMatch(collation,@"^[a-zA-Z0-9_]+$"))throw new Exception("Unexpected ID definition. Nothing changed.");
 return type+" CHARACTER SET "+charset+" COLLATE "+collation;
}
schema=schema.Replace("__TICKET_ID_DEFINITION__",await Definition("tickets")).Replace("__CONTACT_ID_DEFINITION__",await Definition("contacts"));
`;
migration=migration.replace(marker,definitions+marker);
migration=migration.replace('Console.WriteLine("Additional ticket-user storage ready.',`await using var shape=new MySqlCommand("SELECT ticket_id,contact_id FROM ticket_additional_users LIMIT 0",connection);
await using var shapeReader=await shape.ExecuteReaderAsync();await shapeReader.DisposeAsync();
Console.WriteLine("Additional ticket-user storage ready.`);
await fs.writeFile(path.join(folder,'migrate-multiple-ticket-users.py'),migration);
let deploy=await read('deploy-role-deletion.sh');
deploy=deploy.replaceAll('deploy-role-deletion.sh','deploy-multiple-ticket-users.sh')
 .replace('# Deploy role deletion controls only. No migration, role deletion, data copies or backups.','# Deploy multiple ticket users and automatic self-assignment. No backups or data copies.');
deploy=deploy.replace('for file in ','for file in "$source_backend/FeatureMultipleTicketUsers.cs" "$source_backend/local-maintenance/multiple-ticket-users.sql" "$source_backend/local-maintenance/migrate-multiple-ticket-users.py" ');
deploy=deploy.replace('systemctl stop "$service"','python3 "$source_backend/local-maintenance/migrate-multiple-ticket-users.py" --apply\n\nsystemctl stop "$service"');
deploy=deploy.replace("echo 'DEPLOYED: Delete role button between Refresh and Save, with confirmation and assigned-role protection.'","echo 'DEPLOYED: Select multiple ticket users; the signed-in eligible technician is selected automatically.'")
 .replace("echo 'Backend health: OK. No roles, users or tickets were deleted during deployment. No database migration or backups.'","echo 'Backend health: OK. Existing accounts, permissions and tickets retained. No backups.'");
await fs.writeFile(path.join(folder,'deploy-multiple-ticket-users.sh'),deploy);
console.log('Prepared the guarded update and new relationship-table migration. No application data accessed.');
