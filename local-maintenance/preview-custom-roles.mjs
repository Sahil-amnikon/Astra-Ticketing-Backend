// Local UI fixture only: no network APIs, accounts or database writes.
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const front = path.join(root, 'ASTRA-Frontend-latest');
const { createServer } = await import(pathToFileURL(path.join(front, 'node_modules/vite/dist/node/index.js')));
const ts = (await import(pathToFileURL(path.join(front, 'node_modules/typescript/lib/typescript.js')))).default;
const fixture = `import React,{useState} from 'react';
import {createRoot} from 'react-dom/client';
import PermissionWorkspace from '/src/pages/admin/permission-workspace.tsx';
import RecordForm from '/src/pages/admin/record-form.tsx';
import SelectMenus from '/src/pages/select-menus.tsx';
import '/src/pages/globals.css';import '/src/pages/spark-ui-theme.css';import '/src/pages/dark-mode.css';
const codes=['TICKETS_TEAM_VIEW','TICKETS_ASSIGN','TICKETS_APPROVE','CATEGORY_MANAGE','SLA_MANAGE','REPORTS_VIEW','USERS_MANAGE','CLIENTS_MANAGE','CONTACTS_MANAGE','SITES_MANAGE','TAGS_MANAGE','AUDIT_VIEW'];
let roles=['USER','INTERN','TECHNICIAN','TEAM_LEAD','MANAGER','ADMIN'].map(code=>({code,permissions:code==='TECHNICIAN'?['TICKETS_TEAM_VIEW']:code==='TEAM_LEAD'?['TICKETS_TEAM_VIEW','TICKETS_ASSIGN']:['ADMIN','MANAGER'].includes(code)?codes:[],available:code==='TECHNICIAN'?['TICKETS_TEAM_VIEW','REPORTS_VIEW']:code==='TEAM_LEAD'?codes.filter(c=>!c.includes('MANAGE')):['ADMIN','MANAGER'].includes(code)?codes:[],defaults:[],revision:1,editable:['TECHNICIAN','TEAM_LEAD'].includes(code),users:0}));
for(const [name,users,id]of [['Unused support role',0,'22222222-2222-4222-8222-222222222222'],['Assigned support role',2,'33333333-3333-4333-8333-333333333333']])roles.push({...roles.find(r=>r.code==='TECHNICIAN'),code:'custom-'+id,name,baseRole:'TECHNICIAN',custom:true,users});
const request=async(url,options)=>{if(url==='/admin/access-policies/roles'&&options?.method==='POST'){const body=JSON.parse(options.body);if(roles.some(r=>r.name?.toLowerCase()===body.name.toLowerCase()))throw Error('A role with this name already exists.');const source=roles.find(r=>r.code===body.baseRole);const role={...source,code:'custom-11111111-1111-4111-8111-111111111111',name:body.name,baseRole:body.baseRole,custom:true,users:0};roles=[...roles,role];return role}
if(options?.method==='DELETE'){const code=url.split('/').at(-1);const role=roles.find(r=>r.code===code);if(!role)throw Error('Role no longer exists');if(role.users)throw Error('Reassign users before deleting');roles=roles.filter(r=>r.code!==code);return {deleted:true}}if(url==='/admin/access-policies')return {roles};if(options?.method==='PATCH')return {saved:true};return {items:[],total:0}};
function App(){const [roleTarget,setRoleTarget]=useState(null);const [people,setPeople]=useState(false);return <main className="admin-shell" style={{padding:24}}><div className="page-heading"><div><h1>People &amp; Permissions</h1><p>Manage people, role permissions and individual access.</p></div><div className="role-heading-action" ref={setRoleTarget}/></div><p>Sample data only.</p><button onClick={()=>setPeople(!people)}>Switch preview screen</button>{people?<RecordForm resource="users" isAdmin record={{id:'sample-user',name:'Sample Employee',email:'sample@example.test',roles:['TECHNICIAN'],work_scope:'INTERNAL',active:true}} request={request} onSaved={()=>{}} onCancel={()=>setPeople(false)}/>:<PermissionWorkspace mode="role" request={request} addRoleTarget={roleTarget}/>}<SelectMenus/></main>}
document.documentElement.classList.add('spark-ui');createRoot(document.getElementById('root')).render(<App/>);`;
const server = await createServer({root:front,server:{host:'127.0.0.1',port:3218,strictPort:true},plugins:[{
  name:'access-dropdown-fixture',resolveId(id){if(id==='/__access-fixture.tsx')return '\0access-fixture.tsx'},
  load(id){if(id==='\0access-fixture.tsx')return ts.transpileModule(fixture,{compilerOptions:{jsx:ts.JsxEmit.ReactJSX,module:ts.ModuleKind.ESNext,target:ts.ScriptTarget.ES2022}}).outputText},
  configureServer(vite){vite.middlewares.use('/__custom-role-preview',async(_req,res)=>{res.setHeader('Content-Type','text/html');res.end(await vite.transformIndexHtml('/__custom-role-preview','<!doctype html><html><head></head><body><div id="root"></div><script type="module" src="/__access-fixture.tsx"></script></body></html>'))})}
}]});
await server.listen();console.log('Sample custom role preview: http://127.0.0.1:3218/__custom-role-preview');
