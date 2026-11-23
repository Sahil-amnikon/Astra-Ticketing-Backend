import fs from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../..');
const patches=[{"file":"ASTRA-Frontend-latest/src/pages/knowledge/knowledge-details.tsx","before":"  environment: string; prerequisites: string; keywords: string; reviewDate: string;","after":"  environment: string; prerequisites: string; keywords: string; reviewDate: string;\n  contentTitle?: string; errorMessage?: string;"},{"file":"ASTRA-Frontend-latest/src/pages/knowledge/knowledge-details.tsx","before":"return { scope: value('articleScope')","after":"return { contentTitle: value('contentTitle'), errorMessage: value('errorMessage'), scope: value('articleScope')"},{"file":"ASTRA-Frontend-latest/src/pages/knowledge/knowledge-details.tsx","before":"const entries = [['Scope',","after":"const entries = [['Title',details.contentTitle],['Error message',details.errorMessage],['Scope',"},{"file":"ASTRA-Frontend-latest/src/pages/knowledge/knowledge-content.tsx","before":"  return <div className=\"kb-editor\">\n    <label>Article content","after":"  return <div className=\"kb-editor\">\n    <label>Title<input name=\"contentTitle\" maxLength={300} defaultValue={initial.details?.contentTitle ?? ''} placeholder=\"A short title for the issue or solution\" /></label>\n    <label>Error message<textarea name=\"errorMessage\" rows={3} maxLength={2000} defaultValue={initial.details?.errorMessage ?? ''} placeholder=\"Paste the exact error message or error code, if applicable\" /></label>\n    <label>Article content"},{"file":"ASTRA-Backend-latest/FeatureKnowledgeReview.cs","before":"    public string ReviewDate { get; init; } = \"\";","after":"    public string ReviewDate { get; init; } = \"\";\n    public string ContentTitle { get; init; } = \"\";\n    public string ErrorMessage { get; init; } = \"\";"},{"file":"ASTRA-Backend-latest/FeatureKnowledgeReview.cs","before":"Keywords=FText(input.Keywords??\"\",\"Keywords\",0,500),ReviewDate=date};","after":"Keywords=FText(input.Keywords??\"\",\"Keywords\",0,500),ReviewDate=date,ContentTitle=FText(input.ContentTitle??\"\",\"Content title\",0,300),ErrorMessage=FText(input.ErrorMessage??\"\",\"Error message\",0,2000)};"}];
if(!['--check','--apply'].includes(process.argv[2])) throw Error('Use --check or --apply');
const prepared=new Map();
for(const patch of patches){
 const file=path.join(root,patch.file);let source=prepared.get(file)??(await fs.readFile(file,'utf8')).replace(/\r\n/g,'\n');
 if(!source.includes(patch.after)){
  if(patch.before===null)source+=patch.after;
  else {if(source.split(patch.before).length!==2)throw Error('STOP: article issue fields source differs. No files changed.');source=source.replace(patch.before,patch.after);}
 }
 prepared.set(file,source);
}
if(process.argv[2]==='--apply')for(const [file,source] of prepared)await fs.writeFile(file,source);
console.log('Checked: article title and error message fields. No schema changes.');
