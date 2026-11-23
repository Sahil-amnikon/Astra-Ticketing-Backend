export function routingTestTables(db){
 db.function('JSON_LENGTH',value=>value==null?0:JSON.parse(value).length);
 db.function('JSON_QUOTE',value=>JSON.stringify(value));
 db.function('JSON_CONTAINS',(json,value)=>json==null?0:Number(JSON.parse(json).includes(JSON.parse(value))));
 db.function('JSON_OVERLAPS',(left,right)=>Number(JSON.parse(left).some(value=>JSON.parse(right).includes(value))));
 db.exec(`CREATE TABLE IF NOT EXISTS user_sites(user_id TEXT,site_id TEXT);
 CREATE TABLE IF NOT EXISTS contacts(id TEXT,name TEXT,email TEXT,user_id TEXT,client_id TEXT,site_id TEXT,active INTEGER);
 CREATE TABLE IF NOT EXISTS change_request_details(change_id TEXT PRIMARY KEY,site_ids TEXT,department_ids TEXT,requester_kind TEXT,requester_id TEXT,approver_user_id TEXT);`);
}
