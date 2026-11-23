import {createRequire} from 'node:module';import {fileURLToPath,pathToFileURL} from 'node:url';import path from 'node:path';import assert from 'node:assert/strict';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../../ASTRA-Frontend-latest'),require=createRequire(path.join(root,'package.json'));
const React=require('react'),{renderToStaticMarkup}=require('react-dom/server'),{createServer}=await import(pathToFileURL(require.resolve('vite')).href);
const server=await createServer({root,server:{middlewareMode:true},appType:'custom'});
try{
 const {parseKnowledge,KnowledgeContent,KnowledgeEditor,mediaMarker}=await server.ssrLoadModule('/src/pages/knowledge/knowledge-content.tsx');
 assert.equal(parseKnowledge('Existing plain article').text,'Existing plain article');
 const id='a'.repeat(64),body='<script>unsafe()</script>'+mediaMarker+JSON.stringify([{id,name:'guide.png',type:'image/png',size:42}]);
 assert.equal(parseKnowledge(body).files.length,1);
 const html=renderToStaticMarkup(React.createElement(KnowledgeContent,{body,id:'article'}));assert.ok(html.includes('&lt;script&gt;'));assert.ok(!html.includes('<script>'));assert.ok(html.includes('/api/knowledge/article/files/'+id));assert.ok(!html.includes('ASTRA_KB_MEDIA'));
 const editor=renderToStaticMarkup(React.createElement(KnowledgeEditor,{body,onFiles(){}}));assert.ok(editor.includes('type="file"'));assert.ok(editor.includes('multiple=""'));assert.ok(editor.includes('guide.png'));assert.ok(!editor.includes('name="body" required'));
 const {default:Review}=await server.ssrLoadModule('/src/pages/knowledge/knowledge-review.tsx');const review=renderToStaticMarkup(React.createElement(Review,{csrfToken:'test',revision:0,organizations:[],onReviewed(){}}));for(const label of ['Review KB','Awaiting review','All history','Organization','Submitted from','Submitted through','Title or author'])assert.ok(review.includes(label),label);
 const detailsModule=await server.ssrLoadModule('/src/pages/knowledge/knowledge-details.tsx');
 const details={scope:'CLIENT_SPECIFIC',articleType:'TROUBLESHOOTING',product:'Microsoft 365',version:'Outlook desktop',environment:'PRODUCTION',prerequisites:'Signed-in work account',keywords:'outlook, email',reviewDate:'2027-01-15'};
 const full=detailsModule.detailsMarker+JSON.stringify(details)+detailsModule.textMarker+body;
 assert.deepEqual(parseKnowledge(full).details,details);assert.equal(parseKnowledge(full).files.length,1);assert.equal(parseKnowledge(full).text,'<script>unsafe()</script>');
 const read=renderToStaticMarkup(React.createElement(KnowledgeContent,{body:full,id:'article'}));assert.ok(read.includes('Microsoft 365'));assert.ok(read.includes('Outlook desktop'));assert.ok(!read.includes('ASTRA_KB_DETAILS'));
 const specs=renderToStaticMarkup(React.createElement(detailsModule.KnowledgeSpecifications,{details}));for(const text of ['Product / service','Version / platform','Environment','Prerequisites','Keywords','Next review date'])assert.ok(specs.includes(text),text);
 const scope=renderToStaticMarkup(React.createElement(detailsModule.KnowledgeScopeFields,{details}));assert.ok(scope.includes('General guidance'));assert.ok(scope.includes('Client-specific guidance'));
 console.log('PASS: existing text, image/file rendering, escaped pasted content, protected asset URLs, attachment controls and KB review filters render correctly. No live requests.');
}finally{await server.close();}
