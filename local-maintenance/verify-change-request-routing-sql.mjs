import fs from 'node:fs';import path from 'node:path';import {fileURLToPath} from 'node:url';import assert from 'node:assert/strict';import {DatabaseSync} from 'node:sqlite';import {routingTestTables} from './routing-test-tables.mjs';
const sql=JSON.parse(fs.readFileSync(process.argv[2],'utf8'));const db=new DatabaseSync(':memory:');db.function('UTC_TIMESTAMP',precision=>'2026-11-20 12:00:00');routingTestTables(db);
db.exec(`CREATE TABLE users(id TEXT PRIMARY KEY,display_name TEXT,active INTEGER,access_expires_at TEXT);CREATE TABLE user_roles(user_id TEXT,role_code TEXT);CREATE TABLE client_users(user_id TEXT,client_id TEXT);CREATE TABLE sites(id TEXT,name TEXT,client_id TEXT,active INTEGER);
CREATE TABLE changes(id TEXT PRIMARY KEY,change_number TEXT,title TEXT,client_id TEXT,site_id TEXT,owner_user_id TEXT,requested_by_user_id TEXT,change_type TEXT,status TEXT,planned_start TEXT,planned_end TEXT,affected_resources TEXT);
CREATE TABLE submission_reviews(kind TEXT,entity_id TEXT,submitted_by_user_id TEXT,status TEXT);CREATE TABLE change_blackouts(id TEXT,name TEXT,client_id TEXT,site_id TEXT,active INTEGER,starts_at TEXT,ends_at TEXT);
INSERT INTO sites VALUES('drinkpak','DrinkPak','geometry',1),('ashoc','A Shoc','geometry',1),('ravid-site','Ravid Client','ravid',1);`);
function user(id,role,team,active=1,expires=null){db.prepare('INSERT INTO users VALUES(?,?,?,?)').run(id,id,active,expires);db.prepare('INSERT INTO user_roles VALUES(?,?)').run(id,role);db.prepare('INSERT INTO client_users VALUES(?,?)').run(id,team);}
user('creator','TECHNICIAN','geometry');user('geo-tl','TEAM_LEAD','geometry');user('ravid-tl','TEAM_LEAD','ravid');user('other-geo','TECHNICIAN','geometry');user('manager','MANAGER','amnikon');user('admin','ADMIN','amnikon');user('client-user','USER','geometry');user('ravid-user','USER','ravid');user('expired','USER','geometry',1,'2020-01-01');user('inactive','USER','geometry',0);
db.prepare('INSERT INTO contacts VALUES(?,?,?,?,?,?,?)').run('drinkpak-contact','DrinkPak User','user@drinkpak.com','client-user','geometry','drinkpak',1);
db.prepare('INSERT INTO changes VALUES(?,?,?,?,?,?,?,?,?,?,?,?)').run('change','CHG-001','Patching','geometry','drinkpak','creator','creator','NORMAL','SUBMITTED','2026-11-20 12:00:00','2026-11-20 13:00:00','[]');
db.prepare('INSERT INTO change_request_details VALUES(?,?,?,?,?,?)').run('change','["drinkpak"]','[]','USER','creator','client-user');db.prepare('INSERT INTO submission_reviews VALUES(?,?,?,?)').run('CHANGE','change','creator','PENDING');
const selected=db.prepare('SELECT c.id FROM changes c WHERE c.id=@id AND '+sql.selected);
const visible=db.prepare('SELECT c.id FROM changes c JOIN submission_reviews r ON r.entity_id=c.id WHERE c.id=@id AND '+sql.visible);
const decide=db.prepare('SELECT c.id FROM changes c JOIN submission_reviews r ON r.entity_id=c.id WHERE c.id=@id AND '+sql.decide);
assert.equal(selected.all({id:'change',actor:'client-user'}).length,1,'Linked site user can be chosen to approve their own site');
assert.equal(decide.all({id:'change',actor:'client-user'}).length,1);
for(const actor of ['geo-tl','manager','admin']){assert.equal(visible.all({id:'change',actor}).length,1,actor+' receives visibility');assert.equal(decide.all({id:'change',actor}).length,0,actor+' cannot replace the selected approver');}
for(const actor of ['ravid-tl','ravid-user','other-geo'])assert.equal(visible.all({id:'change',actor}).length,0,actor+' has no designated or observer access');
db.exec("UPDATE change_request_details SET site_ids='[\"drinkpak\",\"ashoc\"]'");assert.equal(selected.all({id:'change',actor:'client-user'}).length,0,'DrinkPak user cannot access A Shoc in a multi-site request');assert.equal(visible.all({id:'change',actor:'client-user'}).length,0);
db.exec("UPDATE change_request_details SET site_ids='[\"drinkpak\"]',requester_kind='CONTACT',requester_id='drinkpak-contact'");assert.equal(decide.all({id:'change',actor:'client-user'}).length,0,'Requester account cannot approve its own Normal change');
db.exec("UPDATE change_request_details SET requester_kind='USER',requester_id='creator',approver_user_id='creator'");assert.equal(decide.all({id:'change',actor:'creator'}).length,0);
db.exec("UPDATE changes SET change_type='STANDARD'");assert.equal(decide.all({id:'change',actor:'creator'}).length,1,'Standard permits self-approval');
db.exec("UPDATE changes SET change_type='NORMAL'; UPDATE change_request_details SET approver_user_id='ravid-user'");assert.equal(selected.all({id:'change',actor:'ravid-user'}).length,0,'Forged cross-organization approver rejected');
for(const actor of ['inactive','expired']){db.prepare('UPDATE change_request_details SET approver_user_id=?').run(actor);assert.equal(selected.all({id:'change',actor}).length,0);}
db.exec("UPDATE change_request_details SET approver_user_id='client-user',site_ids='[]'");assert.equal(selected.all({id:'change',actor:'client-user'}).length,0,'Site-restricted user cannot approve organization-wide data');
db.exec("UPDATE change_request_details SET site_ids='[\"drinkpak\"]'; UPDATE contacts SET active=0");assert.equal(selected.all({id:'change',actor:'client-user'}).length,0,'Inactive site link cannot expand access');
// Execute the actual scheduling SQL, with MySQL JSON casts removed for SQLite.
db.exec("UPDATE change_request_details SET site_ids='[\"drinkpak\",\"ashoc\"]'; INSERT INTO changes VALUES('overlap','CHG-002','Other work','geometry','ashoc','other-geo','other-geo','NORMAL','SCHEDULED','2026-11-20 12:00:00','2026-11-20 13:00:00','[]');INSERT INTO change_blackouts VALUES('blackout','A Shoc maintenance','geometry','ashoc',1,'2026-11-20 12:00:00','2026-11-20 13:00:00');");
const source=fs.readFileSync(path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../FeatureChangeActions.cs'),'utf8');
const conflict=source.match(/"(SELECT id,change_number,title,planned_start,planned_end,'Overlapping site[^"\n]+)"/)[1].replaceAll('CAST(@sites AS JSON)','@sites').replaceAll('CAST(@resources AS JSON)','@resources');
const blackout=source.match(/"(SELECT id,name,starts_at,ends_at FROM change_blackouts[^"\n]+)"/)[1].replaceAll('CAST(@sites AS JSON)','@sites');
const args={id:'change',client:'geometry',sites:'["drinkpak","ashoc"]',start:'2026-11-20 12:00:00',end:'2026-11-20 13:00:00',owner:'creator',resources:'[]'};
assert.equal(db.prepare(conflict).all(args).length,1,'Conflict on the second selected site must be found');
const blackoutQuery=db.prepare(blackout);blackoutQuery.setAllowUnknownNamedParameters(true);assert.equal(blackoutQuery.all(args).length,1,'Blackout on the second selected site must be found');
db.close();console.log('PASS: selected-person-only Normal approval, Standard self-approval, Geometry/Ravid isolation, DrinkPak-only contact access, inactive/expired denial, full multi-site coverage and second-site scheduling conflicts. In-memory fixtures only.');
