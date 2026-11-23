import fs from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {spawnSync} from 'node:child_process';
const patches=[{"path":"ASTRA-Frontend-latest/src/pages/ticket-information.css","before":".ticket-information-grid dt { margin: 0 0 5px; font-size: 12px; color: var(--spark-muted, #61728d); line-height: 1.4; }\n.ticket-information-grid dd { margin: 0; font-size: 13px; font-weight: 600; color: var(--spark-text, #18244a); line-height: 1.5; overflow-wrap: anywhere; }","after":".ticket-information-grid dt { margin: 0 0 5px; font-size: 12px; font-weight: 600; color: var(--spark-text, #18244a); line-height: 1.4; }\n.ticket-information-grid dd { margin: 0; font-size: 13px; font-weight: 400; color: var(--spark-muted, #61728d); line-height: 1.5; overflow-wrap: anywhere; }\nhtml.spark-ui body .panel.ticket-claim-panel { margin-bottom: 18px; }"},{"path":"ASTRA-Frontend-latest/src/pages/ticket-workspace.tsx","before":"{canAssign && !ticket.assigned_user_id ? (\n        <div className=\"panel\">","after":"{canAssign && !ticket.assigned_user_id ? (\n        <div className=\"panel ticket-claim-panel\">"}];
if(!['--check','--apply'].includes(process.argv[2]))throw Error('Use --check or --apply.');
const apply=process.argv[2]==='--apply';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../..');
const norm=s=>s.replace(/\r\n/g,'\n').trimEnd()+'\n';
const changes=new Map();
for(const item of patches){
 const file=path.join(root,item.path);const current=norm(await fs.readFile(file,'utf8'));
 if(current.includes(item.after))continue;
 if(current.split(item.before).length!==2)throw Error('STOP: source differs in '+item.path+'. No files changed.');
 changes.set(file,current.replace(item.before,item.after));
}
for(const [file,next]of changes){
 if(apply)await fs.writeFile(file,next);
 console.log((apply?'Updated: ':'Would update: ')+path.relative(root,file));
}
console.log('Ticket information style check passed. No database changes.');
if(apply){
 const result=spawnSync(process.platform==='win32'?'npm.cmd':'npm',['run','build'],{cwd:path.join(root,'ASTRA-Frontend-latest'),stdio:'inherit',shell:process.platform==='win32'});
 if(result.error)throw result.error;if(result.status!==0)process.exit(result.status??1);
}

