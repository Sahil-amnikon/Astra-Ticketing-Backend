// Frontend-only Add role dialog and heading layout update.
import fs from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {spawnSync} from 'node:child_process';
const patches=[{"path":"ASTRA-Frontend-latest/src/pages/admin/custom-roles.css","before":".custom-role-dialog::backdrop { background: #14224966; }","after":".custom-role-dialog::backdrop { background: #14224966; }\nhtml.spark-ui body dialog.custom-role-dialog { position: fixed; inset: 0; margin: auto; height: fit-content; }\n.page-heading .role-heading-action { align-self: flex-start; flex-shrink: 0; margin-left: auto; }"},{"path":"ASTRA-Frontend-latest/src/pages/admin/admin-console.tsx","before":"  const [accessTab, setAccessTab] = useState<'role' | 'user' | 'people'>(\n    'role',\n  );","after":"  const [accessTab, setAccessTab] = useState<'role' | 'user' | 'people'>(\n    'role',\n  );\n  const [roleActionTarget, setRoleActionTarget] = useState<HTMLDivElement | null>(null);"},{"path":"ASTRA-Frontend-latest/src/pages/admin/admin-console.tsx","before":"            {session &&\n            section !== 'overview' &&","after":"            {session && section === 'users' && accessTab === 'role' ? <div className=\"role-heading-action\" ref={setRoleActionTarget} /> : null}\n            {session &&\n            section !== 'overview' &&"},{"path":"ASTRA-Frontend-latest/src/pages/admin/admin-console.tsx","before":"                    mode={accessTab}\n                    request={request}","after":"                    mode={accessTab}\n                    request={request}\n                    addRoleTarget={roleActionTarget}"},{"path":"ASTRA-Frontend-latest/src/pages/admin/permission-workspace.tsx","before":"import { useEffect, useState } from 'react';","after":"import { useEffect, useState } from 'react';\nimport { createPortal } from 'react-dom';"},{"path":"ASTRA-Frontend-latest/src/pages/admin/permission-workspace.tsx","before":"export default function PermissionWorkspace({\n  request,\n  mode,\n}: {\n  request: Request;\n  mode: 'role' | 'user';","after":"export default function PermissionWorkspace({\n  request,\n  mode,\n  addRoleTarget,\n}: {\n  request: Request;\n  mode: 'role' | 'user';\n  addRoleTarget?: HTMLElement | null;"},{"path":"ASTRA-Frontend-latest/src/pages/admin/permission-workspace.tsx","before":"  return (\n    <section\n      className=\"access-workspace\"","after":"  const addRoleButton = mode === 'role' ? <button type=\"button\" className=\"primary\" disabled={busy || loading} onClick={() => { if (canLeave()) setAddingRole(true); }}>+ Add role</button> : null;\n  return (\n    <section\n      className=\"access-workspace\""},{"path":"ASTRA-Frontend-latest/src/pages/admin/permission-workspace.tsx","before":"        {mode === 'role' ? <button type=\"button\" className=\"primary\" disabled={busy || loading} onClick={() => { if (canLeave()) setAddingRole(true); }}>+ Add role</button> : null}","after":"        {addRoleTarget && addRoleButton ? createPortal(addRoleButton, addRoleTarget) : addRoleButton}"}];
if(!['--check','--apply'].includes(process.argv[2]))throw Error('Use --check or --apply.');
const apply=process.argv[2]==='--apply';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../..');
const norm=s=>s.replace(/\r\n/g,'\n').trimEnd()+'\n';
const read=async file=>norm(await fs.readFile(file,'utf8'));
const changes=new Map();
for(const item of patches){
 const file=path.join(root,item.path);let text=changes.get(file)??await read(file);
 if(text.includes(item.after)){changes.set(file,text);continue}
 if(text.split(item.before).length!==2)throw Error('STOP: Add role layout source differs in '+item.path+'. No files changed.');
 changes.set(file,text.replace(item.before,item.after));
}
for(const [file,next]of changes)if(await read(file)!==next){
 if(apply)await fs.writeFile(file,next);
 console.log((apply?'Updated: ':'Would update: ')+path.relative(root,file));
}
console.log('Add role layout preflight passed. No database or backend changes.');
if(apply){
 const result=spawnSync(process.platform==='win32'?'npm.cmd':'npm',['run','build'],{cwd:path.join(root,'ASTRA-Frontend-latest'),stdio:'inherit',shell:process.platform==='win32'});
 if(result.error)throw result.error;if(result.status!==0)process.exit(result.status??1);
}

