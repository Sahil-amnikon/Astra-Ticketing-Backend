import fs from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../..');
const file=path.join(root,'ASTRA-Frontend-latest/src/pages/change-plan-template-dialog.tsx');
if(!['--check','--apply'].includes(process.argv[2]))throw Error('Use --check or --apply.');
let source=(await fs.readFile(file,'utf8')).replace(/\r\n/g,'\n');
const marker='      <fieldset disabled={busy} className="change-plan-template-fields">';
const fields=`        <label>Requested by<input readOnly value={requestedBy || 'Signed-in user'} /></label>
        <label>Approved by<input readOnly value={direct ? 'Not required — saved directly' : 'Pending review after submission'} /></label>`;
if(!source.includes(fields)){
 if(source.split(marker).length!==2||source.includes('<label>Requested by')||source.includes('<label>Approved by'))throw Error('STOP: popup fields differ. No files changed.');
 source=source.replace(marker,marker+'\n'+fields);
}
source=source.replace('({ roles, request, onClose, onSaved }: Props)','({ roles, requestedBy, request, onClose, onSaved }: Props)');
const before='<label className="wide">Title<input name="title" required minLength={3} maxLength={200} /></label>';
const after='<label className="wide">Change title<input name="title" required minLength={3} maxLength={200} /></label>';
if(!source.includes(after)){
 if(source.split(before).length!==2)throw Error('STOP: template title differs. No files changed.');
 source=source.replace(before,after);
}
if(process.argv[2]==='--apply')await fs.writeFile(file,source);
console.log(process.argv[2]==='--apply'?'Restored: template requester, approver and Change title.':'Checked: template popup undo.');
