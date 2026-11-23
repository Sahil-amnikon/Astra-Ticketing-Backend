import fs from 'node:fs';import assert from 'node:assert/strict';import {DatabaseSync} from 'node:sqlite';
const policy=JSON.parse(fs.readFileSync(process.argv[2],'utf8')),db=new DatabaseSync(':memory:');
db.exec("PRAGMA foreign_keys=ON;CREATE TABLE tickets(id TEXT PRIMARY KEY);CREATE TABLE categories(id TEXT PRIMARY KEY,active INTEGER);CREATE TABLE ticket_categories(ticket_id TEXT,category_id TEXT,sort_order INTEGER,PRIMARY KEY(ticket_id,category_id),FOREIGN KEY(ticket_id) REFERENCES tickets(id) ON DELETE CASCADE,FOREIGN KEY(category_id) REFERENCES categories(id) ON DELETE CASCADE);INSERT INTO categories VALUES('one',1),('two',1),('inactive',0);INSERT INTO tickets VALUES('legacy');");
const check=db.prepare(policy.check.replace(' FOR SHARE','')),insert=db.prepare(policy.sql);
function create(ticket,ids){
 db.exec('BEGIN');try{db.prepare('INSERT INTO tickets VALUES(?)').run(ticket);
  if(check.all({category0:ids[0],category1:ids[1]}).length!==ids.length)throw Error('Unavailable category');
  ids.forEach((category,position)=>insert.run({ticket,category,position}));db.exec('COMMIT');
 }catch(error){db.exec('ROLLBACK');throw error;}
}
create('multi',['one','two']);assert.deepEqual(db.prepare("SELECT category_id FROM ticket_categories WHERE ticket_id='multi' ORDER BY sort_order").all().map(row=>row.category_id),['one','two']);
assert.throws(()=>create('invalid',['one','inactive']),/Unavailable/);assert.equal(db.prepare("SELECT COUNT(*) AS n FROM tickets WHERE id='invalid'").get().n,0);
assert.equal(db.prepare("SELECT COUNT(*) AS n FROM ticket_categories WHERE ticket_id='invalid'").get().n,0);
assert.throws(()=>insert.run({ticket:'multi',category:'one',position:2}),/UNIQUE/);
assert.equal(db.prepare("SELECT COUNT(*) AS n FROM tickets WHERE id='legacy'").get().n,1,'Existing ticket is retained without backfill');
db.prepare("DELETE FROM tickets WHERE id='multi'").run();assert.equal(db.prepare('SELECT COUNT(*) AS n FROM ticket_categories').get().n,0);
db.close();console.log('PASS: actual category query/insert SQL saves multiple unique categories, rejects inactive choices, rolls back failures and retains legacy tickets. In-memory fixtures only.');
