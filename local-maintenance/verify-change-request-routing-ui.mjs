import {createRequire} from 'node:module';import {fileURLToPath,pathToFileURL} from 'node:url';import path from 'node:path';import assert from 'node:assert/strict';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../../ASTRA-Frontend-latest'),require=createRequire(path.join(root,'package.json'));
const React=require('react'),{renderToStaticMarkup}=require('react-dom/server'),{createServer}=await import(pathToFileURL(require.resolve('vite')).href);
const server=await createServer({root,server:{middlewareMode:true},appType:'custom'});
try{
 const {default:Fields,defaultChangeOrganization,eligibleChangeApprovers}=await server.ssrLoadModule('/src/pages/change-request-fields.tsx');
 assert.equal(defaultChangeOrganization([{id:'00000000-0000-4000-8000-000000000132',status:'ACTIVE'},{id:'geometry',status:'ACTIVE'}]),'geometry');
 assert.equal(defaultChangeOrganization([{id:'ravid'}]),'ravid');assert.equal(defaultChangeOrganization([{id:'geometry'},{id:'ravid'}]),'');
 const users=[{id:'creator',name:'Al Amin',site_ids:[]},{id:'lead',name:'TL',site_ids:[]},{id:'client',name:'DrinkPak User',site_ids:['drinkpak']}];
 assert.ok(eligibleChangeApprovers(users,['drinkpak'],'STANDARD','creator','creator','USER:creator',[]).some(user=>user.id==='creator'));
 assert.ok(!eligibleChangeApprovers(users,['drinkpak'],'NORMAL','creator','creator','USER:creator',[]).some(user=>user.id==='creator'));
 assert.ok(!eligibleChangeApprovers(users,['drinkpak','ashoc'],'NORMAL','creator','creator','USER:creator',[]).some(user=>user.id==='client'));
 assert.ok(!eligibleChangeApprovers(users,['drinkpak'],'NORMAL','creator','creator','CONTACT:contact',[{id:'contact',user_id:'client'}]).some(user=>user.id==='client'));
 const html=renderToStaticMarkup(React.createElement(Fields,{user:{id:'creator',displayName:'Al Amin'},clients:[{id:'geometry',name:'Geometry'}],sites:[{id:'drinkpak',name:'DrinkPak',client_id:'geometry'}],departments:[],clientId:'geometry',siteIds:['drinkpak'],onOrganization(){},onSites(){},changeType:'STANDARD',request(){throw Error('No requests allowed');}}));
 for(const label of ['Organization','Site','Department','Owner','Requested by','Approver','Al Amin'])assert.ok(html.includes(label),label);
 assert.ok(!html.includes('Lead department'));assert.ok(!html.includes('Approved by'));
 const {default:Dialog}=await server.ssrLoadModule('/src/pages/change-plan-template-dialog.tsx');const template=renderToStaticMarkup(React.createElement(Dialog,{roles:['TECHNICIAN'],requestedBy:'Al Amin',request(){throw Error('No requests allowed');},onClose(){},onSaved(){}}));assert.ok(!template.includes('EMERGENCY'));assert.ok(template.includes('Submit for approval'));
 console.log('PASS: organization defaults, multi-site identity fields, logged-in Owner, type-aware approver choices and no Emergency for new templates. No requests executed.');
}finally{await server.close();}
