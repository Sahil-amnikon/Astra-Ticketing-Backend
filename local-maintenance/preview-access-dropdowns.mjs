// Local UI fixture only: no network APIs, accounts or database writes.
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const front = path.join(root, 'ASTRA-Frontend-latest');
const { createServer } = await import(pathToFileURL(path.join(front, 'node_modules/vite/dist/node/index.js')));
const ts = (await import(pathToFileURL(path.join(front, 'node_modules/typescript/lib/typescript.js')))).default;
const fixture = `import React,{useState} from 'react';
import {createRoot} from 'react-dom/client';
import RecordPicker from '/src/pages/admin/directory-record-picker.tsx';
import SelectMenus from '/src/pages/select-menus.tsx';
import '/src/pages/globals.css';
import '/src/pages/spark-ui-theme.css';
import '/src/pages/dark-mode.css';
const records=Array.from({length:125},(_,n)=>({id:'member-'+n,name:n===0?'Geometry':n===1?'Ravid IT':'Department '+(n+1),active:true}));
const request=async url=>{const p=new URL(url,'http://fixture').searchParams;const q=(p.get('q')||'').toLowerCase();const rows=records.filter(r=>r.name.toLowerCase().includes(q)||r.id===q);const page=+(p.get('page')||1);const size=+(p.get('pageSize')||50);return {items:rows.slice((page-1)*size,page*size),total:rows.length}};
function App(){const [value,setValue]=useState('');const [message,setMessage]=useState('');const [disabled,setDisabled]=useState(false);
return <main className="admin-shell" style={{padding:32,maxWidth:850,margin:'auto'}}><section className="panel editor"><h1>Access dropdown check</h1><p>Preview uses sample options only.</p>
<form onSubmit={e=>{e.preventDefault();setMessage('Saved '+value)}}><fieldset disabled={disabled} style={{border:0,padding:0,display:'grid',gap:20}}>
<RecordPicker resource="departments" label="Department" value={value} onChange={setValue} request={request} allLabel="All active departments" onSelectAll={()=>setMessage('All 125 departments selected')}/>
<label>Work scope<select><option>Client work only</option><option>All assigned work</option></select></label>
<button type="submit">Save fixture</button></fieldset></form><p role="status">{message}</p>
<button onClick={()=>setDisabled(!disabled)}>Toggle disabled</button><button onClick={()=>document.documentElement.dataset.theme=document.documentElement.dataset.theme==='dark'?'light':'dark'}>Toggle theme</button>
<button onClick={()=>setMessage('Outside clicked')}>Outside</button><SelectMenus/></section></main>}
document.documentElement.classList.add('spark-ui');createRoot(document.getElementById('root')).render(<App/>);`;
const server = await createServer({root:front,server:{host:'127.0.0.1',port:3217,strictPort:true},plugins:[{
  name:'access-dropdown-fixture',resolveId(id){if(id==='/__access-fixture.tsx')return '\0access-fixture.tsx'},
  load(id){if(id==='\0access-fixture.tsx')return ts.transpileModule(fixture,{compilerOptions:{jsx:ts.JsxEmit.ReactJSX,module:ts.ModuleKind.ESNext,target:ts.ScriptTarget.ES2022}}).outputText},
  configureServer(vite){vite.middlewares.use('/__access-preview',async(_req,res)=>{res.setHeader('Content-Type','text/html');res.end(await vite.transformIndexHtml('/__access-preview','<!doctype html><html><head></head><body><div id="root"></div><script type="module" src="/__access-fixture.tsx"></script></body></html>'))})}
}]});
await server.listen();console.log('Sample dropdown preview: http://127.0.0.1:3217/__access-preview');
