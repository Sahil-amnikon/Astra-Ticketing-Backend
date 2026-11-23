import { DatabaseSync } from 'node:sqlite';
import fs from 'node:fs';
import assert from 'node:assert/strict';
const policy = JSON.parse(fs.readFileSync(process.argv[2], 'utf8'));
const db = new DatabaseSync(':memory:');
db.function('UTC_TIMESTAMP', precision => '2026-11-19 00:00:00');
db.exec(`CREATE TABLE users(id TEXT PRIMARY KEY,active INTEGER,access_expires_at TEXT);
CREATE TABLE user_roles(user_id TEXT,role_code TEXT);
CREATE TABLE client_users(user_id TEXT,client_id TEXT);
CREATE TABLE submission_reviews(kind TEXT,entity_id TEXT,submitted_by_user_id TEXT,status TEXT);
CREATE TABLE entities(id TEXT);`);
const root='00000000-0000-4000-8000-000000000132';
function user(id,role,teams,active=1,expiry=null){
 db.prepare('INSERT INTO users VALUES(?,?,?)').run(id,active,expiry);
 db.prepare('INSERT INTO user_roles VALUES(?,?)').run(id,role);
 for(const team of teams)db.prepare('INSERT INTO client_users VALUES(?,?)').run(id,team);
}
user('geo-tech','TECHNICIAN',['geometry',root]);user('ravid-tech','TECHNICIAN',['ravid',root]);
user('internal-tech','TECHNICIAN',[root]);user('geo-tl','TEAM_LEAD',['geometry',root]);
user('ravid-tl','TEAM_LEAD',['ravid',root]);user('internal-tl','TEAM_LEAD',[root]);
user('manager','MANAGER',[root]);user('admin','ADMIN',[root]);
user('inactive','TEAM_LEAD',['geometry'],0);user('expired','ADMIN',[root],1,'2020-01-01');
const check=db.prepare(`SELECT ${policy.reviewer} AS allowed`);
const independent=db.prepare(`SELECT (@actor<>@author AND ${policy.reviewer}) AS allowed`);
assert.equal(independent.get({actor:'geo-tl',author:'geo-tl'}).allowed,0,'a promoted author cannot approve their own submission');
assert.equal(independent.get({actor:'geo-tl',author:'geo-tech'}).allowed,1,'an independent team lead can approve');
for(const [actor,author,expected]of [
 ['geo-tl','geo-tech',1],['geo-tl','ravid-tech',0],['ravid-tl','geo-tech',0],['ravid-tl','ravid-tech',1],
 ['internal-tl','geo-tech',0],['internal-tl','internal-tech',1],['geo-tl','internal-tech',0],
 ['geo-tech','geo-tech',0],['geo-tech','ravid-tech',0],['manager','geo-tech',1],['manager','ravid-tech',1],
 ['admin','internal-tech',1],['inactive','geo-tech',0],['expired','geo-tech',0]
])assert.equal(check.get({actor,author}).allowed,expected,`${actor} reviewing ${author}`);
db.exec("INSERT INTO entities VALUES('pending'),('approved'),('rejected'),('existing'); INSERT INTO submission_reviews VALUES('KNOWLEDGE','pending','geo-tech','PENDING'),('KNOWLEDGE','approved','geo-tech','APPROVED'),('KNOWLEDGE','rejected','geo-tech','REJECTED');");
const visible=db.prepare(`SELECT e.id FROM entities e WHERE ${policy.visible} ORDER BY e.id`);
for(const actor of ['ravid-tech','internal-tech','ravid-tl'])assert.deepEqual(visible.all({actor}).map(r=>r.id),['approved','existing'],actor+' must not see unreleased content');
for(const actor of ['geo-tech','geo-tl','manager','admin'])assert.equal(visible.all({actor}).length,4,actor+' can inspect own/team submissions');
db.prepare("UPDATE submission_reviews SET status='APPROVED' WHERE entity_id='pending'").run();
assert.equal(visible.all({actor:'ravid-tech'}).some(r=>r.id==='pending'),true,'approval releases visibility');
db.prepare("UPDATE submission_reviews SET status='PENDING' WHERE entity_id='pending'").run();
assert.equal(visible.all({actor:'ravid-tech'}).some(r=>r.id==='pending'),false,'editing and resubmission removes release');
console.log('PASS: real policy SQL isolates teams, excludes inactive/expired reviewers, hides pending/rejected submissions and releases only approved content. SQLite fixtures only; no application data.');
if(policy.templateVisible){
 db.exec("CREATE TABLE change_template_reviews(kind TEXT,entity_id TEXT,submitted_by_user_id TEXT,status TEXT); INSERT INTO change_template_reviews VALUES('CHANGE_TEMPLATE','pending','geo-tech','PENDING');");
 const templates=db.prepare(`SELECT e.id FROM entities e WHERE ${policy.templateVisible}`);
 assert.equal(templates.all({actor:'ravid-tech'}).some(row=>row.id==='pending'),false);
 assert.equal(templates.all({actor:'geo-tech'}).some(row=>row.id==='pending'),true);
 db.exec("UPDATE change_template_reviews SET status='APPROVED' WHERE kind='CHANGE_TEMPLATE'");
 assert.equal(templates.all({actor:'ravid-tech'}).some(row=>row.id==='pending'),true);
 console.log('PASS: change templates use independent review and remain hidden from other technicians until approval.');
}
db.exec('CREATE TABLE notifications(user_id TEXT NOT NULL,type TEXT NOT NULL,title TEXT NOT NULL,body TEXT NOT NULL,event_key TEXT NOT NULL UNIQUE);');
assert.throws(()=>db.prepare("INSERT INTO notifications(user_id,type,title,body) VALUES('geo-tl','SUBMISSION_REVIEW','Review','Body')").run(),/NOT NULL/,'Reproduce the missing event_key failure');
const notify=db.prepare(policy.notificationSql);
db.exec('BEGIN');
for(const [index,key]of policy.keys.entries())notify.run({user:'reviewer-'+index,type:index===2?'SUBMISSION_DECIDED':'SUBMISSION_REVIEW',title:'Review',body:'Body',key});
db.exec('COMMIT');
assert.equal(db.prepare('SELECT COUNT(*) AS count FROM notifications').get().count,policy.keys.length);
assert.throws(()=>notify.run({user:'reviewer-0',type:'SUBMISSION_REVIEW',title:'Review',body:'Body',key:policy.keys[0]}),/UNIQUE/);
console.log('PASS: reproduced missing event_key; actual fixed notification SQL commits reviewer and decision alerts with unique event keys. In-memory database only.');
db.close();
