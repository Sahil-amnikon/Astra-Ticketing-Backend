import fs from 'node:fs/promises';import path from 'node:path';import {fileURLToPath} from 'node:url';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../..');
const file=path.join(root,'ASTRA-Frontend-latest/src/pages/change-workspace.tsx');
const before="              {session && view === 'new' && canCreateChangePlan(session.user.roles) ? <button type=\"button\" className=\"primary\" onClick={() => setCreatingTemplate(true)}>New template</button> : null}\n              {session && canReviewCM && view !== 'new' && !reviewingChanges ? <Link className=\"secondary\" href={`/changes?workspace=${encodeURIComponent(selectedWorkspace)}&reviewCM=1`}>Review CM</Link> : null}";
const after="              {session && canReviewCM && view !== 'new' && !reviewingChanges ? <Link className=\"secondary\" href={`/changes?workspace=${encodeURIComponent(selectedWorkspace)}&reviewCM=1`}>Review CM</Link> : null}\n              {session && view === 'dashboard' && !reviewingChanges && canCreateChangePlan(session.user.roles) ? <button type=\"button\" className=\"secondary\" onClick={() => setCreatingTemplate(true)}>New template</button> : null}";
if(!['--check','--apply'].includes(process.argv[2]))throw Error('Use --check or --apply.');
let source=(await fs.readFile(file,'utf8')).replace(/\r\n/g,'\n');
if(!source.includes(after)){
 if(source.split(before).length!==2)throw Error('STOP: header actions differ from the reviewed source. No files changed.');
 source=source.replace(before,after);
}
if(process.argv[2]==='--apply')await fs.writeFile(file,source);
console.log(process.argv[2]==='--apply'?'Updated: Review CM, New template, Raise change header order.':'Checked: template header position.');
