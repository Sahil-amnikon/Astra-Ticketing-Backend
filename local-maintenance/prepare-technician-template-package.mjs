import fs from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
const folder=path.dirname(fileURLToPath(import.meta.url)),root=path.resolve(folder,'../..');
const read=async name=>(await fs.readFile(path.join(folder,name),'utf8')).replace(/\r\n/g,'\n');
const files=[];
for(const name of ['ASTRA-Backend-latest/FeatureChangeTemplateAuthors.cs','ASTRA-Frontend-latest/src/pages/change-template-workspace.tsx'])files.push({path:name,content:(await fs.readFile(path.join(root,name),'utf8')).replace(/\r\n/g,'\n')});
let updater=await read('apply-technician-change-templates.mjs');
if(!updater.includes('const files='))updater=updater.replace('const changes=new Map();','const files='+JSON.stringify(files)+';\nconst changes=new Map();');
else updater=updater.replace(/^const files=.*;$/m,()=> 'const files='+JSON.stringify(files)+';');
if(!updater.includes('for(const item of files)'))updater=updater.replace("if(!['--check','--apply'].includes(process.argv[2]))",`for(const item of files){
 const file=path.join(root,item.path);let current=null;try{current=(await fs.readFile(file,'utf8')).replace(/\\r\\n/g,'\\n')}catch(e){if(e.code!=='ENOENT')throw e}
 if(current!==null&&current!==item.content)throw Error('STOP: template helper differs in '+item.path+'. No files changed.');changes.set(file,item.content);
}
if(!['--check','--apply'].includes(process.argv[2]))`);
await fs.writeFile(path.join(folder,'apply-technician-change-templates.mjs'),updater);
let runner=await read('repair-employee-users.py');
const start=runner.indexOf("source = r'''"),end=runner.indexOf("\n'''",start);
if(start<0||end<0)throw Error('Existing runner differs.');
runner=runner.slice(0,start)+"source = (Path(__file__).resolve().parent/'change-template-review-migration.cs').read_text()"+runner.slice(end+4);
runner=runner.replaceAll('repair-employee-users.py','migrate-technician-change-templates.py').replaceAll('ASTRA_EMPLOYEE_CONNECTION','ASTRA_TEMPLATE_REVIEW_CONNECTION')
 .replaceAll('.employee-user-repair-code-','.template-review-migration-code-').replace('Associate company User-profile employees with Amnikon.','Enable change-template submissions in the reviewed approval schema.');
await fs.writeFile(path.join(folder,'migrate-technician-change-templates.py'),runner);
let deploy=await read('deploy-role-deletion.sh');
deploy=deploy.replaceAll('deploy-role-deletion.sh','deploy-technician-change-templates.sh')
 .replace('# Deploy role deletion controls only. No migration, role deletion, data copies or backups.','# Deploy technician change-template authoring with independent review. No data copies or backups.')
 .replace('for file in ','for file in "$source_backend/FeatureChangeTemplateAuthors.cs" "$source_backend/FeatureSubmissionReviews.cs" "$source_backend/local-maintenance/change-template-review-migration.cs" "$source_backend/local-maintenance/migrate-technician-change-templates.py" ')
 .replace('systemctl stop "$service"','python3 "$source_backend/local-maintenance/migrate-technician-change-templates.py" --apply\n\nsystemctl stop "$service"')
 .replace("echo 'DEPLOYED: Delete role button between Refresh and Save, with confirmation and assigned-role protection.'","echo 'DEPLOYED: Technicians can create change templates and submit them for independent review.'")
 .replace("echo 'Backend health: OK. No roles, users or tickets were deleted during deployment. No database migration or backups.'","echo 'Backend health: OK. Approval policy, CAB and blackout permissions retained. No backups.'");
await fs.writeFile(path.join(folder,'deploy-technician-change-templates.sh'),deploy);
console.log('Prepared guarded technician template update. No application data accessed.');
