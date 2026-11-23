import assert from 'node:assert/strict';
import path from 'node:path';
import {fileURLToPath,pathToFileURL} from 'node:url';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../..');
const front=path.join(root,'ASTRA-Frontend-latest');
const {createServer}=await import(pathToFileURL(path.join(front,'node_modules/vite/dist/node/index.js')));
const React=await import(pathToFileURL(path.join(front,'node_modules/react/index.js')));
const {renderToStaticMarkup}=await import(pathToFileURL(path.join(front,'node_modules/react-dom/server.node.js')));
const server=await createServer({root:front,server:{middlewareMode:true},appType:'custom'});
try{
  const {default:RecordForm}=await server.ssrLoadModule('/src/pages/admin/record-form.tsx');
  const render=(email,roles=['USER'])=>renderToStaticMarkup(React.createElement(RecordForm,{resource:'users',isAdmin:true,
    record:{id:'fixture',name:'Sample User',email,roles,work_scope:'CLIENT',client_ids:['geometry-fixture'],site_ids:[],active:true},
    request:async()=>{throw Error('No API expected during static verification')},onSaved(){},onCancel(){}}));
  const employee=render('mahinoor@amnikontechnologies.com');
  assert(employee.includes('Company: Amnikon'));
  assert(!employee.includes('>MSP access<'));
  assert(!employee.includes('>Client coverage<'));
  assert(!employee.includes('Client user'));
  const external=render('requester@drinkpak.com');
  assert(!external.includes('Company: Amnikon'));
  assert(external.includes('>MSP access<'));
  const admin=render('hamza.rajmohammed@amnikontechnologies.com',['ADMIN']);
  assert(admin.includes('Administrator — all workspaces and all MSPs'));
  assert(!admin.includes('Company: Amnikon'));
  const manager=render('manager@amnikontechnologies.com',['MANAGER']);
  assert(manager.includes('Manager — Amnikon'));
  assert(manager.includes('Company: Amnikon.'));
  assert(!manager.includes('>MSP access<'));
  console.log('PASS: User label, automatic Amnikon company, no customer assignment fields for employee requesters, preserved external requester setup and administrator access. No database accessed.');
} finally {await server.close();}
