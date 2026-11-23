import fs from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../..');
const patches=[{"file":"ASTRA-Frontend-latest/src/pages/change-review-workspace.tsx","before":"<div className=\"change-review-modal-backdrop\">","after":"<div className=\"change-review-modal-backdrop\" onClick={event => { if (event.target === event.currentTarget && !busy) onClose(); }}>"},{"file":"ASTRA-Frontend-latest/src/pages/change-review-workspace.tsx","before":"  function closeReview(){if(busy)return;detailRequest.current++;setSelected(undefined);setDetail(undefined);setDetailLoading(false);setComment('');setError('');}","after":"  function resetReview(){detailRequest.current++;setSelected(undefined);setDetail(undefined);setDetailLoading(false);setComment('');setError('');}\n  function closeReview(){if(!busy)resetReview();}"},{"file":"ASTRA-Frontend-latest/src/pages/change-review-workspace.tsx","before":"      setSelected(undefined); setDetail(undefined); setComment(''); setRefresh(value => value + 1);","after":"      resetReview(); setRefresh(value => value + 1);"},{"file":"ASTRA-Frontend-latest/src/pages/change-review-workspace.css","before":null,"after":"\n/* Keep review labels bold even when the popup is portalled outside the workspace. */\nhtml body .change-review-dialog h3,\nhtml body .change-review-dialog .change-review-facts dt,\nhtml body .change-review-dialog .change-review-decision > label {\n  font-weight: 700;\n  color: var(--spark-text, #18244a);\n}\nhtml body .change-review-dialog h3 { font-size: 14px; margin: 20px 0 8px; }\n"}];
if(!['--check','--apply'].includes(process.argv[2])) throw Error('Use --check or --apply');
const prepared=new Map();
for(const patch of patches){
 const file=path.join(root,patch.file);let source=prepared.get(file)??(await fs.readFile(file,'utf8')).replace(/\r\n/g,'\n');
 if(!source.includes(patch.after)){
  if(patch.before===null)source+=patch.after;
  else {if(source.split(patch.before).length!==2)throw Error('STOP: change review source differs. No files changed.');source=source.replace(patch.before,patch.after);}
 }
 prepared.set(file,source);
}
const reviewSource=prepared.get(path.join(root,'ASTRA-Frontend-latest/src/pages/change-review-workspace.tsx'));
if(!reviewSource?.includes('function resetReview()'))throw Error('STOP: review reset helper is missing. No files changed.');
if(process.argv[2]==='--apply')for(const [file,source] of prepared)await fs.writeFile(file,source);
console.log('Checked: bold review headings and review dialog dismissal. Frontend only.');
