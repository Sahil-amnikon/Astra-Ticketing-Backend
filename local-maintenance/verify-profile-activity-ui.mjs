import {createRequire} from 'node:module';import {fileURLToPath,pathToFileURL} from 'node:url';import path from 'node:path';import assert from 'node:assert/strict';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../../ASTRA-Frontend-latest'),require=createRequire(path.join(root,'package.json')),React=require('react'),{renderToStaticMarkup}=require('react-dom/server'),{createServer}=await import(pathToFileURL(require.resolve('vite')).href);
const server=await createServer({root,server:{middlewareMode:true},appType:'custom'});
try{
 const {default:Activity,ProfileActivityCard}=await server.ssrLoadModule('/src/pages/profile/profile-activity.tsx');
 const card=renderToStaticMarkup(React.createElement(ProfileActivityCard,{label:'Articles created',value:4,hint:'1 awaiting review',active:true,onClick(){}}));for(const text of ['<button','aria-pressed="true"','Articles created','1 awaiting review','View details'])assert.ok(card.includes(text),text);
 const metrics={resolvedToday:2,average30:'1.2',openAssigned:3,qa:'80%'};
 for(const role of ['TECHNICIAN','TEAM_LEAD','MANAGER','ADMIN']){const html=renderToStaticMarkup(React.createElement(Activity,{request(){throw Error('No live requests allowed');},roles:[role],workspace:'operations',offset:330,metrics}));for(const text of ['Resolved today','Daily average','Open assigned tickets','Average QA rating','Changes created','Problems created','Articles created','Services created','My requests'])assert.ok(html.includes(text),role+': '+text);assert.equal((html.match(/class="panel profile-activity-card/g)??[]).length,9,'All nine metric cards are interactive');}
 const user=renderToStaticMarkup(React.createElement(Activity,{request(){throw Error('No live requests allowed');},roles:['USER'],workspace:'client',offset:0,metrics}));assert.ok(user.includes('My requests'));assert.ok(!user.includes('Changes created'));assert.ok(!user.includes('Average QA rating'));
 console.log('PASS: all nine staff profile cards are real accessible buttons; user accounts receive their own-request card, and approval hints render without live requests.');
}finally{await server.close();}
