import fs from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
const maintenance=path.dirname(fileURLToPath(import.meta.url));
const root=path.resolve(maintenance,'../..');
const read=async file=>(await fs.readFile(file,'utf8')).replace(/\r\n/g,'\n');
const additions=['ASTRA-Backend-latest/FeatureSubmissionReviews.cs','ASTRA-Frontend-latest/src/pages/submission-review-panel.tsx','ASTRA-Frontend-latest/src/pages/submission-review-panel.css'];
const files=[];
for(const name of additions)files.push({path:name,content:await read(path.join(root,name))});
const updater=path.join(maintenance,'apply-submission-reviews.mjs');
let code=await read(updater);
const marker='const changes=new Map();';
if(!code.includes('const files='))code=code.replace(marker,'const files='+JSON.stringify(files)+';\n'+marker);
else code=code.replace(/^const files=.*;$/m,'const files='+JSON.stringify(files)+';');
const hook="if(!['--check','--apply'].includes(process.argv[2]))";
if(!code.includes('for(const item of files)'))code=code.replace(hook,`for(const item of files){
 const file=path.join(root,item.path);let existing=null;
 try{existing=(await fs.readFile(file,'utf8')).replace(/\\r\\n/g,'\\n')}catch(e){if(e.code!=='ENOENT')throw e}
 if(existing!==null && existing!==item.content)throw Error('STOP: new review file differs in '+item.path+'. No files changed.');
 changes.set(file,item.content);
}
`+hook);
await fs.writeFile(updater,code);
let migration=await read(path.join(maintenance,'migrate-custom-roles.py'));
migration=migration.replace('Create storage for administrator-defined roles only.','Create storage for technician submission reviews only.')
 .replaceAll('ASTRA_CUSTOM_ROLE_CONNECTION','ASTRA_SUBMISSION_CONNECTION').replaceAll('ASTRA_CUSTOM_ROLE_SCHEMA','ASTRA_SUBMISSION_SCHEMA')
 .replaceAll('migrate-custom-roles.py','migrate-submission-reviews.py').replaceAll('custom-roles.sql','submission-reviews.sql')
 .replaceAll('new[]{"custom_role_profiles","user_custom_roles"}','new[]{"submission_reviews"}')
 .replaceAll("TABLE_NAME='user_custom_roles'","TABLE_NAME='submission_reviews'")
 .replaceAll('Custom role storage','Submission review storage').replaceAll('Custom role relationship','Submission review relationship')
 .replaceAll('.custom-role-migration-code-','.submission-migration-code-');
migration=migration.replace('Console.WriteLine("Submission review storage ready.',`await using var shape=new MySqlCommand("SELECT kind,entity_id,submitted_by_user_id,status,revision,submitted_at,reviewed_by_user_id,reviewed_at,comment FROM submission_reviews LIMIT 0",connection);
await using var shapeReader=await shape.ExecuteReaderAsync();await shapeReader.DisposeAsync();
Console.WriteLine("Submission review storage ready.`);
await fs.writeFile(path.join(maintenance,'migrate-submission-reviews.py'),migration);
let startup=await read(path.join(maintenance,'verify-role-startup.py'));
startup=startup.replaceAll('MapRoleDeletion','MapSubmissionReviews').replaceAll('endpoints.Length!=1','endpoints.Length!=2')
 .replaceAll('Delete role','Submission review').replaceAll('DELETE role','Submission review').replaceAll('role-endpoint','submission-endpoint');
await fs.writeFile(path.join(maintenance,'verify-submission-startup.py'),startup);
let deploy=await read(path.join(maintenance,'deploy-role-deletion.sh'));
deploy=deploy.replaceAll('deploy-role-deletion.sh','deploy-submission-reviews.sh');
deploy=deploy.replace('# Deploy role deletion controls only. No migration, role deletion, data copies or backups.','# Deploy submission review controls and their new storage table. No data copies or backups.');
deploy=deploy.replace('for file in ', 'for file in "$source_backend/FeatureSubmissionReviews.cs" "$source_backend/local-maintenance/submission-reviews.sql" "$source_backend/local-maintenance/migrate-submission-reviews.py" "$source_backend/local-maintenance/verify-submission-startup.py" ');
deploy=deploy.replace('runuser -u Sahil -- python3 "$source_backend/local-maintenance/verify-role-startup.py" "$stage"','runuser -u Sahil -- python3 "$source_backend/local-maintenance/verify-role-startup.py" "$stage"\nrunuser -u Sahil -- python3 "$source_backend/local-maintenance/verify-submission-startup.py" "$stage"');
deploy=deploy.replace('systemctl stop "$service"','python3 "$source_backend/local-maintenance/migrate-submission-reviews.py" --apply\n\nsystemctl stop "$service"');
deploy=deploy.replace("echo 'DEPLOYED: Delete role button between Refresh and Save, with confirmation and assigned-role protection.'","echo 'DEPLOYED: Technician submissions now require independent review before release.'")
 .replace("echo 'Backend health: OK. No roles, users or tickets were deleted during deployment. No database migration or backups.'","echo 'Backend health: OK. Submission review storage ready; existing users, roles and tickets retained. No backups.'");
await fs.writeFile(path.join(maintenance,'deploy-submission-reviews.sh'),deploy);
console.log('Prepared guarded source updates, review-table migration and application deployment. No application data was accessed.');
