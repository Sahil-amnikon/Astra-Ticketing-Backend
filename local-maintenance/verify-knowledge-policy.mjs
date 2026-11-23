import fs from 'node:fs';import assert from 'node:assert/strict';import {DatabaseSync} from 'node:sqlite';
const sql=JSON.parse(fs.readFileSync(process.argv[2],'utf8')).scope;
const db=new DatabaseSync(':memory:');db.function('UTC_TIMESTAMP',_precision=> '2026-11-20');
db.exec(`CREATE TABLE users(id TEXT,active INTEGER,access_expires_at TEXT);CREATE TABLE user_roles(user_id TEXT,role_code TEXT);CREATE TABLE client_users(user_id TEXT,client_id TEXT);CREATE TABLE articles(id TEXT,author TEXT,client TEXT);INSERT INTO articles VALUES('geometry','author','geometry'),('ravid','author','ravid'),('global','author',NULL);`);
for(const [id,role,client,active] of [['author','TECHNICIAN','geometry',1],['tl','TEAM_LEAD','geometry',1],['wrong-tl','TEAM_LEAD','ravid',1],['manager','MANAGER','amnikon',1],['admin','ADMIN','amnikon',1],['inactive','TEAM_LEAD','geometry',0]]){db.prepare('INSERT INTO users VALUES(?,?,NULL)').run(id,active);db.prepare('INSERT INTO user_roles VALUES(?,?)').run(id,role);db.prepare('INSERT INTO client_users VALUES(?,?)').run(id,client);}
const rows=db.prepare('SELECT e.id FROM articles e WHERE '+sql);
assert.deepEqual(rows.all({actor:'tl'}).map(x=>x.id),['geometry']);
for(const actor of ['wrong-tl','author','inactive'])assert.equal(rows.all({actor}).length,0);
for(const actor of ['manager','admin'])assert.equal(rows.all({actor}).length,3);
db.prepare('INSERT INTO client_users VALUES(?,?)').run('author','ravid');assert.deepEqual(rows.all({actor:'wrong-tl'}).map(x=>x.id),['ravid']);assert.deepEqual(rows.all({actor:'tl'}).map(x=>x.id),['geometry']);
console.log('PASS: KB reviewer policy isolates organization, matches the author’s team, denies technicians/inactive reviewers, and allows manager/admin oversight. In-memory fixtures only.');
