import {createRequire} from 'node:module';import {fileURLToPath,pathToFileURL} from 'node:url';import path from 'node:path';import assert from 'node:assert/strict';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../../ASTRA-Frontend-latest'),require=createRequire(path.join(root,'package.json'));
const React=require('react'),{renderToStaticMarkup}=require('react-dom/server'),{MemoryRouter}=require('react-router-dom'),{createServer}=await import(pathToFileURL(require.resolve('vite')).href);
const server=await createServer({root,server:{middlewareMode:true},appType:'custom'});
try{
 const {default:Page}=await server.ssrLoadModule('/src/pages/ticket-comment-page.tsx');
 for(const type of ['INTERNAL','PUBLIC']){
  const html=renderToStaticMarkup(React.createElement(MemoryRouter,null,React.createElement(Page,{ticketId:'test',ticketNumber:'TKT-1',subject:'Printer issue',type,backHref:'/operations/tickets/test',request(){throw Error('No live requests allowed');}})));
  assert.ok(html.includes(type==='INTERNAL'?'Save internal note':'Save public reply'));
  assert.ok(html.includes(type==='INTERNAL'?'Visible only to service-desk staff':'Visible to the requester'));
  assert.ok(html.includes('Back to ticket'));assert.equal((html.match(/<textarea/g)||[]).length,1);assert.ok(html.includes('maxLength="20000"'));
  assert.ok(!html.includes(type==='INTERNAL'?'Save public reply':'Save internal note'));
 }
 console.log('PASS: internal note and public reply render separate forms, correct visibility guidance, save buttons and return links. No live requests.');
}finally{await server.close();}
