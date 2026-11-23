import fs from 'node:fs/promises';import path from 'node:path';import {fileURLToPath} from 'node:url';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../..');
const file=path.join(root,'ASTRA-Frontend-latest/src/pages/change-plan-template-dialog.tsx');
const before=`        <label>Requested by<input readOnly value={requestedBy || 'Signed-in user'} /></label>
        <label>Approved by<input readOnly value={direct ? 'Not required — saved directly' : 'Pending review after submission'} /></label>`;
const after=`        <label className="wide">Technician<input readOnly value={requestedBy || 'Signed-in user'} /></label>`;
if(!['--check','--apply'].includes(process.argv[2]))throw Error('Use --check or --apply.');
let source=(await fs.readFile(file,'utf8')).replace(/\r\n/g,'\n');
if(!source.includes(after)){
 if(source.split(before).length!==2)throw Error('STOP: template identity fields differ from the reviewed source. No files changed.');
 source=source.replace(before,after);
}
if(process.argv[2]==='--apply')await fs.writeFile(file,source);
console.log(process.argv[2]==='--apply'?'Updated: template Technician automatically displays the signed-in name.':'Checked: template technician field.');
