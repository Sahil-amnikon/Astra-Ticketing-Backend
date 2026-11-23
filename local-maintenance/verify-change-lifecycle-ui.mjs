import {createRequire} from 'node:module';import {fileURLToPath,pathToFileURL} from 'node:url';import path from 'node:path';import assert from 'node:assert/strict';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../../ASTRA-Frontend-latest'),require=createRequire(path.join(root,'package.json'));
const React=require('react'),{renderToStaticMarkup}=require('react-dom/server'),{createServer}=await import(pathToFileURL(require.resolve('vite')).href);
const server=await createServer({root,server:{middlewareMode:true},appType:'custom'});
try{
 const {default:Controls}=await server.ssrLoadModule('/src/pages/change-lifecycle-controls.tsx');
 const render=(state,reviewStatus,nextStatuses,canAmend=false)=>renderToStaticMarkup(React.createElement(Controls,{workflow:{reviewStatus,nextStatuses,requestedBy:'Technician',approvedBy:reviewStatus==='APPROVED'?'Team Lead':null,canAmend},change:{id:'fixture',status:state,revision:1},busy:false,submit(){throw Error('No requests allowed');}}));
 assert.ok(!render('SUBMITTED','PENDING',[]).includes('Start work'));
 assert.ok(render('APPROVED','APPROVED',['IN_PROGRESS']).includes('Start work'));
 const working=render('IN_PROGRESS','APPROVED',['COMPLETED','FAILED','ROLLED_BACK']);assert.ok(working.includes('Mark completed'));assert.ok(!working.includes('Close change'));assert.ok(working.includes('Implementation and test results'));
 assert.ok(!render('COMPLETED','APPROVED',[]).includes('Close change'));
 const closure=render('COMPLETED','APPROVED',['CLOSED']);assert.ok(closure.includes('Close change'));assert.ok(closure.includes('Closure review'));
 assert.ok(render('SUBMITTED','REJECTED',[],true).includes('Resubmit for review'));assert.ok(!render('CLOSED','APPROVED',[]).includes('type="submit"'));
 console.log('PASS: pending technicians cannot start; approved owners see Start work and Mark completed; only authorized reviewers see Close change; revisions resubmit; closed changes expose no workflow actions. No requests executed.');
}finally{await server.close();}
