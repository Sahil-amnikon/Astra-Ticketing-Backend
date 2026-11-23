import fs from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../..');
const file=path.join(root,'ASTRA-Frontend-latest/src/pages/change-workspace.tsx');
const before=`          <select name="ownerUserId">
            <option value="">Me</option>
            <Opts items={options.users} />`;
const after=`          <select name="ownerUserId" defaultValue={user.id}>
            <option value={user.id}>{user.displayName}</option>
            <Opts items={options.users.filter(person => String(person.id) !== user.id)} />`;
if(!['--check','--apply'].includes(process.argv[2]))throw Error('Use --check or --apply.');
let source=(await fs.readFile(file,'utf8')).replace(/\r\n/g,'\n');
if(!source.includes("user: Session['user']"))throw Error('STOP: signed-in user context is missing. No files changed.');
if(!source.includes(after)){
 if(source.split(before).length!==2)throw Error('STOP: owner field differs from the reviewed source. No files changed.');
 source=source.replace(before,after);
}
if(process.argv[2]==='--apply')await fs.writeFile(file,source);
console.log(process.argv[2]==='--apply'?'Updated: signed-in change owner name.':'Checked: signed-in change owner name.');
