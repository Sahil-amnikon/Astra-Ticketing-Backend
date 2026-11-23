import fs from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../..');
const file=path.join(root,'ASTRA-Frontend-latest/src/pages/change-workspace.tsx');
const before=`        <label className="wide">
          Change title
          <input required minLength={3} maxLength={200} name="title" />`;
const after=`        <label>
          Requested by
          <input readOnly value={user.displayName} />
        </label>
        <label>
          Approved by
          <input readOnly value="Not yet approved" />
        </label>
        <label className="wide">
          Title
          <input required minLength={3} maxLength={200} name="title" />`;
if(!['--check','--apply'].includes(process.argv[2]))throw Error('Use --check or --apply.');
let source=(await fs.readFile(file,'utf8')).replace(/\r\n/g,'\n');
if(!source.includes("user: Session['user']"))throw Error('STOP: signed-in user context is missing. No files changed.');
if(!source.includes(after)){
 if(source.split(before).length!==2)throw Error('STOP: change title field differs from the reviewed source. No files changed.');
 source=source.replace(before,after);
}
if(process.argv[2]==='--apply')await fs.writeFile(file,source);
console.log(process.argv[2]==='--apply'?'Updated: Requested by, Approved by and Title on the new change form.':'Checked: new change request fields.');
