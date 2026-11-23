import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import path from 'node:path';
import { stripTypeScriptTypes } from 'node:module';
import { fileURLToPath, pathToFileURL } from 'node:url';
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const front = path.join(root, 'ASTRA-Frontend-latest');
const source = await fs.readFile(path.join(front, 'src/pages/admin/user-access-options.ts'), 'utf8');
const code = stripTypeScriptTypes(source).replace("from '../role-access'", `from '${pathToFileURL(path.join(front, 'src/pages/role-access.ts')).href}'`);
const {workScopeForRole, effectiveChangeAccess, allActiveAccessIds, INTERNAL_MSP_ID} = await import('data:text/javascript;base64,' + Buffer.from(code).toString('base64'));
assert.equal(workScopeForRole('MANAGER', 'CLIENT'), 'ALL');
assert.equal(workScopeForRole('ADMIN', 'INTERNAL'), 'ALL');
assert.equal(workScopeForRole('TECHNICIAN', 'ALL'), 'BOTH');
assert.equal(workScopeForRole('TEAM_LEAD', 'CLIENT'), 'CLIENT');
assert.equal(workScopeForRole('INTERN', 'CLIENT'), 'INTERNAL');
assert.equal(workScopeForRole('INTERN', 'BOTH'), 'BOTH');
assert.equal(effectiveChangeAccess('TECHNICIAN', 'NONE'), 'CONTRIBUTOR');
assert.equal(effectiveChangeAccess('TEAM_LEAD', 'NONE'), 'CHANGE_MANAGER');
assert.equal(effectiveChangeAccess('USER', 'CHANGE_MANAGER'), 'NONE');
assert.equal(effectiveChangeAccess('MANAGER', 'NONE'), 'CHANGE_MANAGER');
const id = n => `00000000-0000-4000-8000-${n.toString().padStart(12,'0')}`;
const calls = [];
const ids = await allActiveAccessIds('clients', async url => {
  calls.push(url);
  const page = Number(new URL(url, 'http://fixture.invalid').searchParams.get('page'));
  return page === 1 ? {items: Array.from({length:100},(_,n)=>({id:id(n+1)})),total:103}
    : {items:[{id:INTERNAL_MSP_ID},{id:id(101)},{id:id(102)}],total:103};
});
assert.equal(ids.length, 102);
assert.ok(!ids.includes(INTERNAL_MSP_ID));
assert.equal(calls.length, 2);
assert.ok(calls.every(url => url.includes('status=active')));
await assert.rejects(allActiveAccessIds('clients', async url => url.includes('page=1')
  ? {items:Array.from({length:100},(_,n)=>({id:id(n+1)})),total:101}
  : {items:[],total:101}), /complete selection|every option/);
await assert.rejects(allActiveAccessIds('departments', async()=>({items:[{id:'not-an-id'}],total:1})), /invalid identifier/);
await assert.rejects(allActiveAccessIds('clients', async url => url.includes('page=1')
  ? {items:Array.from({length:100},(_,n)=>({id:id(n+1)})),total:101}
  : {items:[{id:id(1)}],total:101}), /changed while loading/);
await assert.rejects(allActiveAccessIds('clients', async()=>{throw new Error('request denied');}), /request denied/);
assert.deepEqual(await allActiveAccessIds('departments',async()=>({items:[],total:0})), []);
console.log('PASS: explicit All selection, complete pagination, internal-MSP exclusion, role-safe scopes, effective change defaults, and no partial selection on errors.');
