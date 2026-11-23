import fs from 'node:fs';import path from 'node:path';import {fileURLToPath} from 'node:url';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../..');
const file=path.join(root,'ASTRA-Frontend-latest/src/pages/change-workspace.tsx');
const before="            <div className=\"v38-row\">\n              {session && canReviewCM && view !== 'new' && !reviewingChanges ?",after="            <div className=\"v38-row\">\n              {view === 'new' ? <Link className=\"secondary\" href={`/changes?workspace=${encodeURIComponent(selectedWorkspace)}`}>Back to CM</Link> : null}\n              {session && canReviewCM && view !== 'new' && !reviewingChanges ?";
const source=fs.readFileSync(file,'utf8').replace(/\r\n/g,'\n');
if(!process.argv.includes('--check')&&!process.argv.includes('--apply'))throw Error('Use --check or --apply');
let result=source;
if(source.split(after).length===2)console.log('Back to CM button already present.');
else if(source.split(before).length===2)result=source.replace(before,after);
else throw Error('STOP: reviewed change header differs. No files written.');
if(process.argv.includes('--apply'))fs.writeFileSync(file,result);
console.log('Back to CM source checks passed. No backups or data copies.');

