import {createRequire} from 'node:module';import {fileURLToPath,pathToFileURL} from 'node:url';import path from 'node:path';import assert from 'node:assert/strict';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../../ASTRA-Frontend-latest'),require=createRequire(path.join(root,'package.json')),{createServer}=await import(pathToFileURL(require.resolve('vite')).href);
const server=await createServer({root,server:{middlewareMode:true},appType:'custom'});
try{
 const {reviewNotificationPath}=await server.ssrLoadModule('/src/pages/review-notification-route.ts');
 for(const type of ['SUBMISSION_REVIEW','CHANGE_APPROVAL_REQUESTED'])for(const [label,target]of [['Review CM','/changes?reviewCM=1'],['Review KB','/knowledge?reviewKB=1'],['Review PM','/problems?reviewPM=1'],['Review services','/catalogue?reviewService=1']])assert.equal(reviewNotificationPath({type,body:'Open '+label}),target);
 assert.equal(reviewNotificationPath({type:'CHANGE_APPROVAL_REQUESTED',body:'Review CM',change_id:'record'}),undefined);
 assert.equal(reviewNotificationPath({type:'CHANGE_APPROVAL_REQUESTED',body:'Review CM',ticket_id:'ticket'}),undefined);
 assert.equal(reviewNotificationPath({type:'CHANGE_STATUS_UPDATED',body:'Review CM'}),undefined);
 assert.equal(reviewNotificationPath({type:'CHANGE_APPROVAL_REQUESTED',body:'No module target'}),undefined);
 console.log('PASS: legacy and compatible review alerts open the correct review queues; linked change/ticket alerts retain their own destinations.');
}finally{await server.close();}
