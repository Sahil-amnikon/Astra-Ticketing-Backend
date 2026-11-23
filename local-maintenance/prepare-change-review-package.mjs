import fs from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
const folder=path.dirname(fileURLToPath(import.meta.url)),root=path.resolve(folder,'../..');
const read=async name=>(await fs.readFile(path.join(folder,name),'utf8')).replace(/\r\n/g,'\n');
const files=[];
for(const name of ['ASTRA-Backend-latest/FeatureChangeReviewQueue.cs','ASTRA-Frontend-latest/src/pages/change-review-workspace.tsx','ASTRA-Frontend-latest/src/pages/change-review-workspace.css','ASTRA-Frontend-latest/src/pages/change-review-query.ts','ASTRA-Frontend-latest/src/pages/change-review-access.ts'])files.push({path:name,content:(await fs.readFile(path.join(root,name),'utf8')).replace(/\r\n/g,'\n')});
let source=await read('apply-change-review-queue.mjs');
if(!source.includes('const files='))source=source.replace('const changes=new Map();','const files='+JSON.stringify(files)+';\nconst changes=new Map();');
else source=source.replace(/^const files=.*;$/m,()=> 'const files='+JSON.stringify(files)+';');
if(!source.includes('for(const item of files)'))source=source.replace("if(!['--check','--apply'].includes(process.argv[2]))",`for(const item of files){
 const file=path.join(root,item.path);let current=null;try{current=(await fs.readFile(file,'utf8')).replace(/\\r\\n/g,'\\n')}catch(e){if(e.code!=='ENOENT')throw e}
 if(current!==null&&current!==item.content)throw Error('STOP: review queue file differs in '+item.path+'. No files changed.');changes.set(file,item.content);
}
if(!['--check','--apply'].includes(process.argv[2]))`);
await fs.writeFile(path.join(folder,'apply-change-review-queue.mjs'),source);
let deploy=await read('deploy-role-deletion.sh');
deploy=deploy.replaceAll('deploy-role-deletion.sh','deploy-change-review-queue.sh')
 .replace('# Deploy role deletion controls only. No migration, role deletion, data copies or backups.','# Deploy the role-protected Change Management review screen. No database migration or backups.')
 .replace('for file in ','for file in "$source_backend/FeatureChangeReviewQueue.cs" "$source_backend/local-maintenance/verify-submission-startup.py" ')
 .replace('runuser -u Sahil -- python3 "$source_backend/local-maintenance/verify-role-startup.py" "$stage"','runuser -u Sahil -- python3 "$source_backend/local-maintenance/verify-role-startup.py" "$stage"\nrunuser -u Sahil -- python3 "$source_backend/local-maintenance/verify-submission-startup.py" "$stage"')
 .replace("echo 'DEPLOYED: Delete role button between Refresh and Save, with confirmation and assigned-role protection.'","echo 'DEPLOYED: Review CM button and filtered waiting/history lists for Team Leads, Managers and Administrators.'")
 .replace("echo 'Backend health: OK. No roles, users or tickets were deleted during deployment. No database migration or backups.'","echo 'Backend health: OK. The Change Management review bar is removed for every role. Existing records retained; no database migration or backups.'");
await fs.writeFile(path.join(folder,'deploy-change-review-queue.sh'),deploy);
console.log('Prepared the guarded Review CM update. No application data accessed.');
