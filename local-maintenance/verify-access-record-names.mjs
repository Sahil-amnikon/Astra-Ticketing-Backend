import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import { stripTypeScriptTypes } from 'node:module';
const file = new URL('../../ASTRA-Frontend-latest/src/pages/admin/access-record-name.ts', import.meta.url);
const code = stripTypeScriptTypes(await fs.readFile(file, 'utf8'));
const {findAccessRecord} = await import('data:text/javascript;base64,'+Buffer.from(code).toString('base64'));
const direct = async () => ({items:[{id:'chosen',name:'Application'}]});
assert.equal((await findAccessRecord(direct,'departments','chosen')).name,'Application');
let pageCalls=0;
const legacy = async url => {
  const params=new URL(url,'http://fixture').searchParams;
  if(params.has('q'))return {items:[{id:'wrong',name:'Do not display'}],total:1};
  pageCalls++;
  return +params.get('page')===1
    ? {items:Array.from({length:100},(_,i)=>({id:'other-'+i,name:'Other'})),total:102}
    : {items:[{id:'geometry',name:'Geometry'},{id:'ravid',name:'Ravid IT'}],total:102};
};
const [geometry,ravid]=await Promise.all([
  findAccessRecord(legacy,'clients','geometry'),findAccessRecord(legacy,'clients','ravid')]);
assert.equal(geometry.name,'Geometry');assert.equal(ravid.name,'Ravid IT');
assert.equal(pageCalls,2,'Both memberships share the paginated permitted directory');
assert.equal(await findAccessRecord(legacy,'clients','missing'),undefined);
await assert.rejects(findAccessRecord(async()=>{throw Error('Forbidden')},'clients','geometry'),/Forbidden/);
console.log('PASS: exact department names, legacy-server fallback, multi-page MSP names, shared lookup, missing IDs and access errors.');
