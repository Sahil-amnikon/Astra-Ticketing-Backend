import {createRequire} from 'node:module';import {fileURLToPath,pathToFileURL} from 'node:url';import path from 'node:path';import assert from 'node:assert/strict';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../../ASTRA-Frontend-latest'),require=createRequire(path.join(root,'package.json'));
const React=require('react'),{renderToStaticMarkup}=require('react-dom/server'),{createServer}=await import(pathToFileURL(require.resolve('vite')).href);
const server=await createServer({root,server:{middlewareMode:true},appType:'custom'});
try{
 const {canReviewChangeRequests}=await server.ssrLoadModule('/src/pages/change-review-access.ts');
 for(const role of ['TEAM_LEAD','MANAGER','ADMIN'])assert.equal(canReviewChangeRequests([role]),true);
 for(const role of ['TECHNICIAN','INTERN','USER'])assert.equal(canReviewChangeRequests([role]),false);
 const {changeReviewQuery,defaultChangeReviewFilter}=await server.ssrLoadModule('/src/pages/change-review-query.ts');
 assert.equal(new URLSearchParams(changeReviewQuery(defaultChangeReviewFilter,1)).get('status'),'PENDING');
 const history=new URLSearchParams(changeReviewQuery({...defaultChangeReviewFilter,status:'APPROVED',q:'  printer  ',clientId:'geometry',from:'2026-11-18',to:'2026-11-18'},2));
 assert.equal(history.get('q'),'printer');assert.equal(history.get('status'),'APPROVED');assert.equal(history.get('page'),'2');assert.equal(history.get('clientId'),'geometry');assert.ok(new Date(history.get('to'))>new Date(history.get('from')));
 assert.throws(()=>changeReviewQuery({...defaultChangeReviewFilter,from:'2026-11-19',to:'2026-11-18'},1));
 const {default:Workspace}=await server.ssrLoadModule('/src/pages/change-review-workspace.tsx');
 const html=renderToStaticMarkup(React.createElement(Workspace,{csrfToken:'fixture',workspace:'operations'}));
 for(const text of ['Review status','Waiting for review','Approved','Changes requested','All reviews','Search changes','MSP','Submitted from','Submitted to'])assert.ok(html.includes(text),text);
 assert.ok(!html.includes('Review submissions ·'));
 console.log('PASS: Review CM is limited to reviewer roles, defaults to waiting, exposes history/date/search/MSP filters and replaces the old review bar. No application requests executed.');
}finally{await server.close();}
