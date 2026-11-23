import fs from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../..');
const file=path.join(root,'ASTRA-Frontend-latest/src/pages/change-plan-template-dialog.tsx');
if(!['--check','--apply'].includes(process.argv[2]))throw Error('Use --check or --apply.');
let source=(await fs.readFile(file,'utf8')).replace(/\r\n/g,'\n');
const fields=`        <label>Requested by<input readOnly value={requestedBy || 'Signed-in user'} /></label>
        <label>Approved by<input readOnly value={direct ? 'Not required — saved directly' : 'Pending review after submission'} /></label>
`;
if(source.includes(fields))source=source.replace(fields,'');
else if(source.includes('<label>Requested by')||source.includes('<label>Approved by'))throw Error('STOP: template identity fields differ. No files changed.');
source=source.replace('({ roles, requestedBy, request, onClose, onSaved }: Props)','({ roles, request, onClose, onSaved }: Props)');
const before='<label className="wide">Change title<input name="title" required minLength={3} maxLength={200} /></label>';
const after='<label className="wide">Title<input name="title" required minLength={3} maxLength={200} /></label>';
if(!source.includes(after)){
 if(source.split(before).length!==2)throw Error('STOP: template title field differs. No files changed.');
 source=source.replace(before,after);
}
if(process.argv[2]==='--apply')await fs.writeFile(file,source);
console.log(process.argv[2]==='--apply'?'Updated: template popup fields and Title label.':'Checked: template popup fields and Title label.');
