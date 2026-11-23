import fs from 'node:fs';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../..');
const target=path.join(root,'ASTRA-Frontend-latest/src/pages/knowledge/knowledge-content.css');
const before='.kb-review-filters{display:grid;grid-template-columns:2fr 1fr 1fr;gap:14px}';
const after='.kb-review-filters{display:grid;grid-template-columns:2fr 1fr 1fr;gap:14px;align-items:end}';
const rules='/* KB filter alignment */\n.kb-review-filters>label{min-width:0}.kb-review-filters>label :is(input,select){min-height:46px}.kb-review-filters>.v38-row{align-self:end;align-items:center;justify-content:flex-start;gap:12px;flex-wrap:wrap;margin:0;padding:0;min-height:46px}.kb-review-filters>.v38-row>button{min-height:46px;margin:0}\n';
let source=fs.readFileSync(target,'utf8');
if(source.includes(before))source=source.replace(before,after);else if(!source.includes(after))throw Error('STOP: KB filter layout differs. No files written.');
if(!source.includes('/* KB filter alignment */'))source+='\n'+rules;
if(process.argv.includes('--apply')){fs.writeFileSync(target,source);console.log('Updated: KB filter fields and action buttons align along the bottom.');}
else if(process.argv.includes('--check'))console.log('PASS: KB filter layout update is ready.');
else throw Error('Use --apply or --check');
