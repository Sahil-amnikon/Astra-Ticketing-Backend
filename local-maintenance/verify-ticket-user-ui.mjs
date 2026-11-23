import {createRequire} from 'node:module';
import {fileURLToPath,pathToFileURL} from 'node:url';
import path from 'node:path';
import assert from 'node:assert/strict';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../../ASTRA-Frontend-latest');
const require=createRequire(path.join(root,'package.json'));
const React=require('react');const {renderToStaticMarkup}=require('react-dom/server');
const {createServer}=await import(pathToFileURL(require.resolve('vite')).href);
const server=await createServer({root,server:{middlewareMode:true},appType:'custom'});
try{
 const {initialTicketTechnician,technicianOptions}=await server.ssrLoadModule('/src/pages/ticket-assignee-default.ts');
 for(const user of [{id:'alamin',displayName:'Alamin Shaikh'},{id:'another',displayName:'Another Technician'}]){
  assert.equal(initialTicketTechnician(null,user.id),user.id);
  assert.equal(initialTicketTechnician('',user.id),user.id);
  assert.deepEqual(technicianOptions([],user),[{id:user.id,name:user.displayName}]);
  assert.equal(initialTicketTechnician('existing-assignee',user.id),'existing-assignee');
  assert.equal(technicianOptions([{id:user.id,name:user.displayName}],user).length,1);
  assert.equal(technicianOptions([],user,{id:'existing-assignee',name:'Existing Technician'})[0].name,'Existing Technician');
 }
 console.log('PASS: every signed-in technician appears immediately before directory loading; existing ticket assignments remain selected.');
 const {default:RequesterSelect}=await server.ssrLoadModule('/src/pages/requester-select.tsx');
 const contacts=[{id:'one',name:'User One',email:'one@example.test'},{id:'two',name:'User Two',email:'two@example.test'}];
 const multiple=renderToStaticMarkup(React.createElement(RequesterSelect,{contacts,value:['one','two'],onChange:()=>{},disabled:false}));
 assert.match(multiple,/2 users selected/);assert.match(multiple,/User One · Primary/);assert.match(multiple,/Remove User One/);assert.match(multiple,/Remove User Two/);
 const {default:TicketInformation}=await server.ssrLoadModule('/src/pages/ticket-information.tsx');
 const information=renderToStaticMarkup(React.createElement(TicketInformation,{ticket:{requester_name:'User One',requester_email:'one@example.test',additional_requesters:[contacts[1]]},history:[]}));
 for(const text of ['User names','User emails','User One, User Two','one@example.test, two@example.test'])assert.ok(information.includes(text),text);
 const existing=renderToStaticMarkup(React.createElement(TicketInformation,{ticket:{requester_name:'Existing User',requester_email:'existing@example.test'},history:[]}));
 assert.ok(existing.includes('Existing User'));assert.ok(!existing.includes('User names'));
 console.log('PASS: multiple selections render removable names and a primary requester; ticket information shows every selected user; existing single-user tickets still render.');
}finally{await server.close();}
