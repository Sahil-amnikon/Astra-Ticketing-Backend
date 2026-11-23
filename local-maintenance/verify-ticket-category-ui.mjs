import {createRequire} from 'node:module';import {fileURLToPath,pathToFileURL} from 'node:url';import path from 'node:path';import assert from 'node:assert/strict';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../../ASTRA-Frontend-latest'),require=createRequire(path.join(root,'package.json')),{createServer}=await import(pathToFileURL(require.resolve('vite')).href);
const React=require('react'),{renderToStaticMarkup}=require('react-dom/server'),server=await createServer({root,server:{middlewareMode:true},appType:'custom'});
try{
 const {ticketCategorySelection}=await server.ssrLoadModule('/src/pages/ticket-category-selection.ts');
 const options=[{id:'hardware',name:'Hardware'},{id:'apps',name:'Applications'},{id:'printer',name:'Hardware → Printer',parent_id:'hardware'},{id:'screen',name:'Hardware → Screen',parent_id:'hardware'},{id:'office',name:'Applications → Office',parent_id:'apps'},{id:'general',name:'General'}];
 assert.deepEqual(ticketCategorySelection(options,['hardware','apps'],['printer','office']),{ids:['printer','office'],primary:'printer'});
 assert.ok(ticketCategorySelection(options,['hardware','apps'],['printer']).error?.includes('Applications'));
 assert.ok(ticketCategorySelection(options,[],[]).error);
 assert.deepEqual(ticketCategorySelection(options,['general'],['office']),{ids:['general'],primary:'general'});
 const allParents=options.filter(item=>!item.parent_id).map(item=>item.id),allChildren=options.filter(item=>item.parent_id).map(item=>item.id);
 assert.deepEqual(ticketCategorySelection(options,allParents,allChildren),{ids:['printer','screen','office','general'],primary:'printer'});
 const {default:Information}=await server.ssrLoadModule('/src/pages/ticket-information.tsx');
 const html=renderToStaticMarkup(React.createElement(Information,{ticket:{category_name:'Original',categories:[{name:'Hardware → Printer'},{name:'Applications → Office'}]},history:[]}));
 assert.ok(html.includes('Hardware → Printer, Applications → Office'));assert.ok(html.includes('Categories'));
 console.log('PASS: multiple parent/subcategory selection, All categories, primary order, required children and saved-category display. No live requests.');
}finally{await server.close();}
