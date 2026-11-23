import fs from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {createHash} from 'node:crypto';
const folder=path.dirname(fileURLToPath(import.meta.url));
const original=(await fs.readFile(path.join(folder,'apply-technician-change-templates.mjs'),'utf8')).replace(/\r\n/g,'\n');
const patches=JSON.parse(original.match(/^const patches=(.*);$/m)[1]).reverse();
const additions=JSON.parse(original.match(/^const files=(.*);$/m)[1]);
const allowed=new Set(['ASTRA-Backend-latest/FeatureChangeTemplateAuthors.cs','ASTRA-Frontend-latest/src/pages/change-template-workspace.tsx']);
if(additions.length!==2||additions.some(file=>!allowed.has(file.path)))throw Error('Unexpected template-only source files. Nothing changed.');
const removals=additions.map(file=>({path:file.path,hash:createHash('sha256').update(file.content.replace(/\r\n/g,'\n')).digest('hex')}));
const code=`import fs from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {createHash} from 'node:crypto';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../..');
const patches=${JSON.stringify(patches)};
const removals=${JSON.stringify(removals)};
if(!['--check','--apply'].includes(process.argv[2]))throw Error('Use --check or --apply.');
const changes=new Map(),remove=[];
for(const p of patches){
 const file=path.join(root,p.path);let source=changes.get(file)??(await fs.readFile(file,'utf8')).replace(/\\r\\n/g,'\\n');
 const added=[p.after,...(p.previous??[])].find(text=>source.split(text).length===2);
 if(added)source=source.replace(added,p.before);
 else if(!source.includes(p.before))throw Error('STOP: rollback source differs in '+p.path+'. No files changed.');
 changes.set(file,source);
}
for(const item of removals){
 const file=path.join(root,item.path);let source;
 try{source=(await fs.readFile(file,'utf8')).replace(/\\r\\n/g,'\\n')}catch(e){if(e.code==='ENOENT')continue;throw e}
 if(createHash('sha256').update(source).digest('hex')!==item.hash)throw Error('STOP: template-only file has unrelated edits: '+item.path+'. No files changed.');
 remove.push(file);
}
for(const[file,source]of changes){if(process.argv[2]==='--apply')await fs.writeFile(file,source);console.log((process.argv[2]==='--apply'?'Restored: ':'Checked: ')+path.relative(root,file));}
for(const file of remove){if(process.argv[2]==='--apply')await fs.unlink(file);console.log((process.argv[2]==='--apply'?'Removed template-only code: ':'Would remove template-only code: ')+path.relative(root,file));}
console.log('Technician template authoring rolled back. No database changes, data copies or backups.');
`;
await fs.writeFile(path.join(folder,'rollback-technician-change-templates.mjs'),code);
let deploy=(await fs.readFile(path.join(folder,'deploy-role-deletion.sh'),'utf8')).replace(/\r\n/g,'\n');
deploy=deploy.replaceAll('deploy-role-deletion.sh','deploy-template-rollback.sh')
 .replace('# Deploy role deletion controls only. No migration, role deletion, data copies or backups.','# Restore administrator-only template management. No database migration, data copies or backups.')
 .replace("echo 'DEPLOYED: Delete role button between Refresh and Save, with confirmation and assigned-role protection.'","echo 'DEPLOYED: Technician template creation removed. Administrator and Manager template management restored.'")
 .replace("echo 'Backend health: OK. No roles, users or tickets were deleted during deployment. No database migration or backups.'","echo 'Backend health: OK. Existing templates, tickets, approvals and database tables retained. No backups.'");
await fs.writeFile(path.join(folder,'deploy-template-rollback.sh'),deploy);
console.log('Prepared guarded rollback for technician template authoring. No application data accessed.');
