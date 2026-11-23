import fs from 'node:fs';import assert from 'node:assert/strict';import {DatabaseSync} from 'node:sqlite';
const policy=JSON.parse(fs.readFileSync(process.argv[2],'utf8'));
const db=new DatabaseSync(':memory:');
// Exact type vocabulary reported by notifications.notifications_check_9.
db.exec(`CREATE TABLE notifications(user_id TEXT NOT NULL,type TEXT NOT NULL CHECK(type IN ('TICKET_ASSIGNED','TICKET_TRANSFERRED','PUBLIC_REPLY','SLA_AT_RISK','SLA_BREACHED','TICKET_UNATTENDED','CHANGE_APPROVAL_REQUESTED','CHANGE_TASK_ASSIGNED','CHANGE_STATUS_UPDATED','HELP_REQUESTED','HELP_ACCEPTED')),title TEXT NOT NULL,body TEXT NOT NULL,event_key TEXT NOT NULL UNIQUE);`);
const insert=db.prepare(policy.sql),base={user:'selected-approver',title:'Review CM',body:'Open Review CM',key:'request'};
assert.throws(()=>insert.run({...base,type:'SUBMISSION_REVIEW'}),/CHECK constraint failed/,'Reproduce the server constraint rejecting the old request type');
assert.throws(()=>insert.run({...base,type:'SUBMISSION_DECIDED'}),/CHECK constraint failed/,'Reproduce the decision failure before the fix');
db.exec('BEGIN');
insert.run({...base,type:policy.request});
insert.run({...base,user:'requester',type:policy.decision,key:'decision',title:'Submission approved'});
db.exec('COMMIT');
assert.equal(db.prepare('SELECT COUNT(*) AS n FROM notifications').get().n,2);
assert.deepEqual(db.prepare('SELECT type FROM notifications ORDER BY event_key').all().map(row=>row.type),['CHANGE_STATUS_UPDATED','CHANGE_APPROVAL_REQUESTED']);
assert.throws(()=>insert.run({...base,type:policy.request}),/UNIQUE/,'Stable event keys still prevent duplicate alerts');
db.close();console.log('PASS: reproduced the reported notification CHECK failure; actual mapped request/decision inserts commit against the same allowed-type constraint. In-memory fixtures only.');
