import fs from 'node:fs/promises';import path from 'node:path';import {fileURLToPath} from 'node:url';
const folder=path.dirname(fileURLToPath(import.meta.url)),root=path.resolve(folder,'../..');
const read=async name=>(await fs.readFile(path.join(folder,name),'utf8')).replace(/\r\n/g,'\n');
const files=[];
for(const name of ['ASTRA-Backend-latest/FeatureChangePlanTemplates.cs','ASTRA-Frontend-latest/src/pages/change-plan-model.ts','ASTRA-Frontend-latest/src/pages/change-plan-template-dialog.tsx','ASTRA-Frontend-latest/src/pages/change-plan-template-dialog.css'])files.push({path:name,content:(await fs.readFile(path.join(root,name),'utf8')).replace(/\r\n/g,'\n')});
let updater=await read('apply-change-form-templates.mjs');
if(!updater.includes('const files='))updater=updater.replace('const changes=new Map();','const files='+JSON.stringify(files)+';\nconst changes=new Map();');
else updater=updater.replace(/^const files=.*;$/m,()=> 'const files='+JSON.stringify(files)+';');
if(!updater.includes('for(const item of files)'))updater=updater.replace("if(!['--check','--apply'].includes(process.argv[2]))",`for(const item of files){const file=path.join(root,item.path);let current=null;try{current=(await fs.readFile(file,'utf8')).replace(/\\r\\n/g,'\\n')}catch(e){if(e.code!=='ENOENT')throw e}if(current!==null&&current!==item.content)throw Error('STOP: template file differs in '+item.path+'. No files changed.');changes.set(file,item.content);}
if(!['--check','--apply'].includes(process.argv[2]))`);
await fs.writeFile(path.join(folder,'apply-change-form-templates.mjs'),updater);
let runner=await read('repair-employee-users.py');
const start=runner.indexOf("source = r'''"),end=runner.indexOf("\n'''",start);
if(start<0||end<0)throw Error('Reviewed VM runner differs.');
runner=runner.slice(0,start)+"source = (Path(__file__).resolve().parent/'change-form-template-storage.cs').read_text()"+runner.slice(end+4);
runner=runner.replaceAll('repair-employee-users.py','migrate-change-form-templates.py').replaceAll('ASTRA_EMPLOYEE_CONNECTION','ASTRA_FORM_TEMPLATE_CONNECTION').replaceAll('.employee-user-repair-code-','.change-form-template-storage-code-').replace('Associate company User-profile employees with Amnikon.','Create full change-form template storage only.');
await fs.writeFile(path.join(folder,'migrate-change-form-templates.py'),runner);
let deploy=await read('deploy-change-review-queue.sh');
deploy=deploy.replaceAll('deploy-change-review-queue.sh','deploy-change-form-templates.sh')
 .replace('# Deploy the role-protected Change Management review screen. No database migration or backups.','# Deploy full change templates with technician approval and direct TL/Manager/Admin saving. No backups.')
 .replace('for file in ','for file in "$source_backend/FeatureChangePlanTemplates.cs" "$source_backend/local-maintenance/change-form-template-storage.cs" "$source_backend/local-maintenance/migrate-change-form-templates.py" ')
 .replace('systemctl stop "$service"','python3 "$source_backend/local-maintenance/migrate-change-form-templates.py" --apply\n\nsystemctl stop "$service"')
 .replace("echo 'DEPLOYED: Review CM button and filtered waiting/history lists for Team Leads, Managers and Administrators.'","echo 'DEPLOYED: Full change templates. Technicians submit for approval; Team Leads, Managers and Administrators save directly.'")
 .replace("echo 'Backend health: OK. The Change Management review bar is removed for every role. Existing records retained; no database migration or backups.'","echo 'Backend health: OK. Template approvals are in Review CM > Templates. Existing records retained; no ALTER or database privilege grants, no backups.'");
await fs.writeFile(path.join(folder,'deploy-change-form-templates.sh'),deploy);
console.log('Prepared full change-template update and CREATE-only storage installer. No application data accessed.');
