import fs from 'node:fs';import assert from 'node:assert/strict';import {DatabaseSync} from 'node:sqlite';
import {routingTestTables} from './routing-test-tables.mjs';
const sql=JSON.parse(fs.readFileSync(process.argv[2],'utf8'));const db=new DatabaseSync(':memory:');
db.function('UTC_TIMESTAMP',precision=>'2026-11-20 12:00:00');
db.exec(`CREATE TABLE users(id TEXT PRIMARY KEY,display_name TEXT,active INTEGER,access_expires_at TEXT);CREATE TABLE user_roles(user_id TEXT,role_code TEXT);CREATE TABLE client_users(user_id TEXT,client_id TEXT);
CREATE TABLE changes(id TEXT PRIMARY KEY,status TEXT,updated_at TEXT,client_id TEXT,owner_user_id TEXT,requested_by_user_id TEXT,change_type TEXT);CREATE TABLE sites(id TEXT,active INTEGER);CREATE TABLE submission_reviews(kind TEXT,entity_id TEXT,submitted_by_user_id TEXT,status TEXT,reviewed_by_user_id TEXT,comment TEXT);
INSERT INTO changes(id,status) VALUES('change','DRAFT');INSERT INTO submission_reviews VALUES('CHANGE','change','tech','PENDING',NULL,NULL);`);
routingTestTables(db);
function user(id,role,team,active=1,expiry=null){db.prepare('INSERT INTO users VALUES(?,?,?,?)').run(id,id,active,expiry);db.prepare('INSERT INTO user_roles VALUES(?,?)').run(id,role);db.prepare('INSERT INTO client_users VALUES(?,?)').run(id,team);}
user('tech','TECHNICIAN','geometry');user('other-tech','TECHNICIAN','geometry');user('geo-tl','TEAM_LEAD','geometry');user('ravid-tl','TEAM_LEAD','ravid');user('manager','MANAGER','amnikon');user('admin','ADMIN','amnikon');user('inactive','TEAM_LEAD','geometry',0);user('expired','MANAGER','amnikon',1,'2020-01-01');
const workflow=db.prepare(sql.workflow);for(const actor of ['geo-tl','manager','admin'])assert.equal(workflow.get({id:'change',actor,owner:'tech'}).can_close,1,actor+' can independently close');
for(const actor of ['tech','other-tech','ravid-tl','inactive','expired'])assert.equal(workflow.get({id:'change',actor,owner:'tech'}).can_close,0,actor+' cannot close');
assert.equal(workflow.get({id:'change',actor:'geo-tl',owner:'geo-tl'}).can_close,0,'An assigned TL cannot close their own work');
db.prepare(sql.submitted).run({id:'change'});assert.equal(db.prepare('SELECT status FROM changes').get().status,'SUBMITTED');
db.prepare(sql.approved).run({id:'change'});db.exec("UPDATE submission_reviews SET status='APPROVED',reviewed_by_user_id='geo-tl'");
assert.equal(db.prepare('SELECT status FROM changes').get().status,'APPROVED');assert.equal(workflow.get({id:'change',actor:'tech',owner:'tech'}).approved_by_name,'geo-tl');
db.exec("UPDATE submission_reviews SET status='PENDING',reviewed_by_user_id=NULL");db.prepare(sql.submitted).run({id:'change'});assert.equal(db.prepare('SELECT status FROM changes').get().status,'SUBMITTED');
db.exec("DELETE FROM submission_reviews; UPDATE changes SET status='DRAFT'");db.prepare(sql.submitted).run({id:'change'});assert.equal(db.prepare('SELECT status FROM changes').get().status,'DRAFT','Legacy records retain their existing workflow');
db.close();console.log('PASS: actual lifecycle SQL enforces team and role limits, independent closure, Submitted-to-Approved state changes and reapproval after revision. In-memory fixtures only.');
