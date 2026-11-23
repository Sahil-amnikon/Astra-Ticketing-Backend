// Isolated browser checks: no application API calls or database changes.
import { fileURLToPath, pathToFileURL } from 'node:url';
import path from 'node:path';
import assert from 'node:assert/strict';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const front = path.join(root, 'ASTRA-Frontend-latest');
const { createServer } = await import(pathToFileURL(path.join(front, 'node_modules/vite/dist/node/index.js')));
const ts = (await import(pathToFileURL(path.join(front, 'node_modules/typescript/lib/typescript.js')))).default;
const runtime = process.env.ASTRA_BROWSER_TEST_RUNTIME;
if (!runtime) throw Error('Set ASTRA_BROWSER_TEST_RUNTIME to the bundled Node dependencies directory.');
const { chromium } = await import(pathToFileURL(path.join(runtime, 'playwright/index.mjs')));
const fixture = `
import React, {StrictMode, useState} from 'react';
import {createRoot} from 'react-dom/client';
import SelectMenus from '/src/pages/select-menus.tsx';
import '/src/pages/globals.css';
import '/src/pages/spark-ui-theme.css';
import '/src/pages/dark-mode.css';
function Options(){return <><option value="noor">Noor</option><option value="amin">Amin</option><option value="rashid">Rashid</option></>}
function App(){const [name,setName]=useState('noor'); const [count,setCount]=useState(0); const [gone,setGone]=useState(false);
return <><form id="test-form" style={{padding:24,maxWidth:650}}><label>Technician{!gone&&<select id="controlled" name="technician" value={name} onChange={e=>{window.currentForm=e.currentTarget.form.id;setName(e.currentTarget.value);setCount(n=>n+1)}}><Options/><optgroup disabled label="Unavailable"><option value="blocked">Blocked</option></optgroup></select>}</label><output id="result">{name}:{count}</output>
<label htmlFor="uncontrolled">Priority</label><select id="uncontrolled" name="priority" required defaultValue=""><option value="" disabled>Select a priority</option><option value="high">High</option><option value="low">Low</option></select>
<fieldset disabled><label>Disabled<select id="disabled"><option>Disabled</option></select></label></fieldset>
<label>Multiple reviewers<select id="multi" name="members" multiple size={3} defaultValue={['a','c']} required><option value="a">A</option><option value="b">B</option><option value="c">C</option></select></label>
<label>Many options<select id="many">{Array.from({length:1000},(_,n)=><option key={n} value={n}>Team member {n}</option>)}</select></label>
<button type="reset" id="reset">Reset</button><button type="button" id="outside">Outside</button><button type="button" id="remove" onClick={()=>setGone(true)}>Remove</button></form>
<div style={{height:900}}/><select id="bottom" aria-label="Bottom choice" style={{margin:24}}><Options/></select><div style={{height:900}}/>
<dialog id="modal"><label>Dialog choice<select id="inside"><Options/></select></label></dialog><SelectMenus/></>}
createRoot(document.getElementById('root')).render(<StrictMode><App/></StrictMode>);
`;
const server = await createServer({ root: front, server: { host: '127.0.0.1', port: 0 }, plugins: [{
  name: 'dropdown-verification',
  resolveId(id) { if (id === '/__dropdown-check.tsx') return '\0dropdown-check.tsx'; },
  load(id) { if (id === '\0dropdown-check.tsx') return ts.transpileModule(fixture, {compilerOptions:{jsx:ts.JsxEmit.ReactJSX,module:ts.ModuleKind.ESNext,target:ts.ScriptTarget.ES2022}}).outputText; },
  configureServer(server) { server.middlewares.use('/__dropdown-check.html', async (_req, res) => {
    res.setHeader('Content-Type','text/html');
    res.end(await server.transformIndexHtml('/__dropdown-check.html', '<html class="spark-ui" data-theme="light"><body><div id="root"></div><script type="module" src="/__dropdown-check.tsx"></script></body></html>'));
  }); },
}] });
let browser;
try {
  await server.listen();
  const url = server.resolvedUrls.local[0] + '__dropdown-check.html';
  browser = await chromium.launch({ executablePath: process.env.ASTRA_BROWSER_EXECUTABLE || 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe', headless: true });
  const page = await browser.newPage({ viewport: { width: 1100, height: 760 } });
  const errors = []; page.on('pageerror', e => errors.push(e.message));
  await page.goto(url); await page.locator('#controlled').waitFor();
  await page.locator('#controlled').click();
  await page.locator('.astra-select-menu').waitFor();
  assert.equal(await page.locator('.astra-select-menu').getAttribute('aria-label'), 'Technician');
  if (process.env.ASTRA_DROPDOWN_PREVIEW) await page.screenshot({path:process.env.ASTRA_DROPDOWN_PREVIEW});
  await page.locator('.astra-select-menu').getByRole('option', { name: 'Amin', exact:true }).click();
  await page.waitForFunction(()=>document.querySelector('#result').textContent==='amin:1');
  assert.equal(await page.evaluate(()=>window.currentForm), 'test-form');
  assert.equal(await page.evaluate(()=>new FormData(document.querySelector('form')).get('technician')), 'amin');
  assert.equal(await page.locator('.astra-select-menu').count(), 0);
  await page.locator('#controlled').click(); await page.locator('.astra-select-menu').getByRole('option',{name:'Amin',exact:true}).click();
  assert.equal(await page.locator('#result').textContent(), 'amin:1');
  await page.locator('#controlled').click();
  assert.equal(await page.locator('.astra-select-menu').getByRole('option',{name:'Blocked',exact:true}).isDisabled(), true);
  await page.locator('#controlled').press('End'); await page.locator('#controlled').press('Enter');
  assert.equal(await page.locator('#result').textContent(), 'rashid:2');
  await page.locator('#controlled').press('Home'); await page.locator('#controlled').press('Enter');
  assert.equal(await page.locator('#result').textContent(), 'noor:3');
  await page.locator('#controlled').press('a'); await page.locator('#controlled').press('Enter');
  assert.equal(await page.locator('#result').textContent(), 'amin:4');
  await page.locator('#controlled').press('F4'); await page.locator('.astra-select-menu').waitFor();
  await page.locator('#controlled').press('ArrowDown'); await page.locator('#controlled').press('Escape');
  assert.equal(await page.locator('#result').textContent(), 'amin:4');
  assert.equal(await page.locator('#controlled').getAttribute('aria-expanded'), null);
  await page.locator('#controlled').click(); await page.locator('#outside').click();
  assert.equal(await page.locator('.astra-select-menu').count(),0);
  assert.equal(await page.locator('#uncontrolled').evaluate(e=>e.checkValidity()),false);
  await page.locator('label[for="uncontrolled"]').click();
  assert.equal(await page.locator('.astra-select-menu').isVisible(),true);
  await page.locator('#uncontrolled').press('Escape');
  await page.locator('#uncontrolled').click(); await page.locator('.astra-select-menu').getByRole('option',{name:'High',exact:true}).click();
  assert.equal(await page.locator('#uncontrolled').evaluate(e=>e.checkValidity()),true);
  assert.equal(await page.evaluate(()=>new FormData(document.querySelector('form')).get('priority')),'high');
  await page.locator('#reset').click(); assert.equal(await page.locator('#uncontrolled').inputValue(),'');
  await page.locator('#uncontrolled').evaluate(e=>{e.value='low'}); await page.locator('#uncontrolled').click();
  assert.equal(await page.locator('.astra-select-menu').getByRole('option',{name:'Low',exact:true}).getAttribute('aria-selected'),'true');
  await page.locator('#uncontrolled').press('Tab'); assert.equal(await page.locator('.astra-select-menu').count(),0);
  assert.deepEqual(await page.evaluate(()=>new FormData(document.querySelector('form')).getAll('members')), ['a','c']);
  await page.locator('#many').click(); await page.locator('#many').press('End');
  assert.equal(await page.locator('.astra-select-menu').evaluate(e=>e.scrollTop>0),true);
  await page.locator('#many').press('Enter'); assert.equal(await page.locator('#many').inputValue(),'999');
  await page.locator('#bottom').evaluate(e=>window.scrollTo(0, e.getBoundingClientRect().top+window.scrollY-window.innerHeight+70));
  await page.locator('#bottom').click(); await page.waitForTimeout(150);
  assert.equal(await page.locator('.astra-select-menu').isVisible(),true);
  const bounds = await page.locator('.astra-select-menu').boundingBox();
  const field = await page.locator('#bottom').boundingBox(); assert.ok(bounds.y>=field.y+field.height);
  await page.locator('#bottom').press('Escape');
  await page.locator('#modal').evaluate(e=>e.showModal()); await page.locator('#inside').click();
  assert.equal(await page.locator('#modal .astra-select-menu').count(),1);
  await page.locator('.astra-select-menu').getByRole('option',{name:'Rashid',exact:true}).click(); assert.equal(await page.locator('#inside').inputValue(),'rashid');
  await page.locator('#modal').evaluate(e=>e.close());
  await page.setViewportSize({width:390,height:740}); await page.locator('#controlled').click();
  const mobileBounds = await page.locator('.astra-select-menu').boundingBox(); assert.ok(mobileBounds.x>=0&&mobileBounds.x+mobileBounds.width<=390);
  await page.evaluate(()=>document.documentElement.dataset.theme='dark');
  const colors = await page.locator('.astra-select-menu').evaluate(e=>({bg:getComputedStyle(e).backgroundColor,fg:getComputedStyle(e).color}));
  assert.notEqual(colors.bg, 'rgb(248, 250, 255)'); assert.notEqual(colors.bg,colors.fg);
  await page.locator('#outside').click();
  await page.locator('#controlled').click(); await page.evaluate(()=>document.querySelector('#remove').click());
  await page.waitForTimeout(50); assert.equal(await page.locator('.astra-select-menu').count(),0);
  assert.deepEqual(errors, []);
  console.log('PASS: selection, React events, FormData, required/reset, disabled groups, keyboard, typeahead, outside close, long lists, bottom positioning, modal, narrow layout, dark theme and unmount.');
  await page.close();
  const mobile = await browser.newContext({ viewport:{width:390,height:740},isMobile:true,hasTouch:true });
  const touchPage = await mobile.newPage(); await touchPage.goto(url); await touchPage.locator('#controlled').tap();
  await touchPage.locator('.astra-select-menu').getByRole('option',{name:'Amin',exact:true}).tap();
  assert.equal(await touchPage.locator('#result').textContent(),'amin:1'); console.log('PASS: touch selection.');
  await touchPage.locator('#many').tap();
  const touchBounds = await touchPage.locator('.astra-select-menu').boundingBox();
  const cdp = await mobile.newCDPSession(touchPage);
  const x = touchBounds.x + touchBounds.width / 2, y = touchBounds.y + touchBounds.height - 20;
  await cdp.send('Input.dispatchTouchEvent',{type:'touchStart',touchPoints:[{x,y}]});
  for(let i=1;i<=8;i++) { await cdp.send('Input.dispatchTouchEvent',{type:'touchMove',touchPoints:[{x,y:y-i*20}]}); await touchPage.waitForTimeout(20); }
  await cdp.send('Input.dispatchTouchEvent',{type:'touchEnd',touchPoints:[]});
  await touchPage.waitForTimeout(100);
  assert.equal(await touchPage.locator('.astra-select-menu').evaluate(e=>e.scrollTop>0),true);
  console.log('PASS: touch scrolling inside long dropdowns.');
  await mobile.close();
} finally { await browser?.close(); await server.close(); }
