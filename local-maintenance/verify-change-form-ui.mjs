import {createRequire} from 'node:module';import {fileURLToPath,pathToFileURL} from 'node:url';import path from 'node:path';import assert from 'node:assert/strict';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../../ASTRA-Frontend-latest'),require=createRequire(path.join(root,'package.json'));
const React=require('react'),{renderToStaticMarkup}=require('react-dom/server'),{createServer}=await import(pathToFileURL(require.resolve('vite')).href);
const server=await createServer({root,server:{middlewareMode:true},appType:'custom'});
try{
 const model=await server.ssrLoadModule('/src/pages/change-plan-model.ts');
 for(const role of ['TEAM_LEAD','MANAGER','ADMIN'])assert.equal(model.canSaveChangePlanDirectly([role]),true);
 for(const role of ['TECHNICIAN','INTERN','USER'])assert.equal(model.canSaveChangePlanDirectly([role]),false);
 assert.equal(model.canCreateChangePlan(['TECHNICIAN']),true);assert.equal(model.canCreateChangePlan(['USER']),false);
 assert.deepEqual(model.templateApprovalNames({requested_by_name:'Al Amin',approved_by_name:'Team Lead',approval_required:1}),{requestedBy:'Al Amin',approvedBy:'Team Lead'});
 assert.equal(model.templateApprovalNames({requested_by_name:'Manager',approval_required:0}).approvedBy,'Not required — saved directly');
 assert.equal(model.templateApprovalNames({}).approvedBy,'Not recorded');
 const form=new FormData();for(const [k,v] of Object.entries({title:'Printer update',description:'Reusable instructions',category:'SERVER',changeType:'NORMAL',risk:'LOW',downtimeMinutes:'15',outageExpected:'true',clientNotificationEnabled:'true',clientId:'private-client',ownerId:'private-owner',requestedBy:'private-user',status:'APPROVED',...Object.fromEntries(model.planTextFields.map(([k])=>[k,'Reusable plan']))}))form.set(k,v);
 const plan=model.changePlanFromForm(form),controls=model.changePlanControlValues({...plan,clientId:'private-client',ownerId:'private-owner'});
 for(const key of ['clientId','ownerId','requestedBy','status']){assert.ok(!(key in plan));assert.ok(!(key in controls));}
 assert.equal(plan.downtimeMinutes,15);assert.equal(controls.outageExpected,'true');for(const [key]of model.planTextFields)assert.equal(controls[key],'Reusable plan');
 const {default:Dialog}=await server.ssrLoadModule('/src/pages/change-plan-template-dialog.tsx');
 for(const role of ['TECHNICIAN','TEAM_LEAD','MANAGER','ADMIN']){
  const html=renderToStaticMarkup(React.createElement(Dialog,{roles:[role],requestedBy:'Example Employee',request:()=>{throw Error('No requests allowed');},onClose(){},onSaved(){}}));
  for(const text of ['Technician','Example Employee'])assert.ok(html.includes(text),text);
  for(const text of ['Requested by','Approved by'])assert.ok(!html.includes(text),text+' removed from template popup');
  assert.ok(html.includes(role==='TECHNICIAN'?'Submit for approval':'Save template'));
  for(const text of ['Template name','Change title','Description','Expected downtime','Planned start','Planned end',...model.planTextFields.map(([,label])=>label)])assert.ok(html.includes(text),text);
  for(const name of ['clientId','ownerId','requestedBy'])assert.ok(!html.includes(`name="${name}"`));
 }
 const {ChangeForm}=await server.ssrLoadModule('/src/pages/change-workspace.tsx');
 const {ChangeTemplateChoices}=await server.ssrLoadModule('/src/pages/change-template-picker.tsx');
 const {MemoryRouter}=await import(pathToFileURL(path.join(path.dirname(require.resolve('react-router-dom')),'index.mjs')).href);
 for(const role of ['TECHNICIAN','INTERN','USER','TEAM_LEAD','MANAGER','ADMIN']){
  const html=renderToStaticMarkup(React.createElement(MemoryRouter,null,React.createElement(ChangeForm,{user:{id:'fixture',name:'Fixture',roles:[role]},templateRevision:0,onTemplateSaved(){},workspace:'operations',options:{clients:[],sites:[],contacts:[],departments:[],users:[],categories:[],types:[],risks:[],tickets:[],statuses:[],tags:[]},submit(){},busy:false,request(){throw Error('No requests allowed');},onError(){}})));
  assert.equal(html.includes('Delete template'),false,'Separate delete button removed');
  assert.ok(!html.includes('Save template'),'The new change form must only submit changes');
  assert.ok(html.includes('>Submit</button>'),'Submit is the main action');
  const choices=renderToStaticMarkup(React.createElement(ChangeTemplateChoices,{templates:[{id:'one',name:'Server update'},{id:'two',name:'Printer update'}],value:'one',choose(){},remove(){},canDelete:model.canSaveChangePlanDirectly([role])}));
  assert.equal((choices.match(/class="change-template-trash"/g)??[]).length,['TEAM_LEAD','MANAGER','ADMIN'].includes(role)?2:0,role+' dustbin visibility');
  assert.ok(!choices.includes('Delete template Start without'));
 }
 console.log('PASS: full template fields, role-specific save/submit/delete controls, complete common-field mapping and exclusion of personal/approval information. No requests executed.');
}finally{await server.close();}
