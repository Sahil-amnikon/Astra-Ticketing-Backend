import fs from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../..');
const patches=[{"file":"ASTRA-Frontend-latest/src/pages/change-workspace.tsx","before":"            </ExpandablePanel>\n          </div>\n        </div>\n      ) : null}\n      {tab === 'approvals' ? (","after":"            </ExpandablePanel>\n            <ExpandablePanel title=\"Activity timeline\" defaultOpen>\n              <p className=\"hint\">Latest activity first</p>\n              {detail.history?.length ? (\n                <div className=\"change-activity-timeline\" role=\"list\" aria-label=\"Change activity\">\n                  {detail.history.map((activity) => (\n                    <article className=\"timeline-item\" role=\"listitem\" key={String(activity.id)}>\n                      <strong>{label(activity.event)}</strong>\n                      <p>{String(activity.actor_name ?? 'System')}</p>\n                      <p>{date(activity.created_at)}</p>\n                    </article>\n                  ))}\n                </div>\n              ) : <p className=\"hint\">No activity has been recorded for this change yet.</p>}\n            </ExpandablePanel>\n          </div>\n        </div>\n      ) : null}\n      {tab === 'approvals' ? ("},{"file":"ASTRA-Frontend-latest/src/pages/change-detail-layout.css","before":null,"after":"\n/* Keep the overview activity feed readable beside the control plan. */\nhtml.spark-ui body .change-workspace-content .change-activity-timeline {\n  max-height: 520px;\n  overflow-y: auto;\n  overscroll-behavior: contain;\n}\nhtml.spark-ui body .change-workspace-content .change-activity-timeline .timeline-item {\n  border-left: 2px solid var(--spark-border, #dce5f2);\n  margin-left: 6px;\n  padding: 0 12px 22px 20px;\n}\nhtml.spark-ui body .change-workspace-content .change-activity-timeline .timeline-item::before {\n  content: '';\n  position: absolute;\n  left: -6px;\n  top: 6px;\n  width: 10px;\n  height: 10px;\n  bottom: auto;\n  border-radius: 50%;\n  background: var(--spark-primary, #2b65d9);\n}\n"}];
if(!['--check','--apply'].includes(process.argv[2])) throw Error('Use --check or --apply');
const prepared=[];
for(const patch of patches){
 const file=path.join(root,patch.file);let source=(await fs.readFile(file,'utf8')).replace(/\r\n/g,'\n');
 if(!source.includes(patch.after)){
  if(patch.before===null)source+=patch.after;
  else {if(source.split(patch.before).length!==2)throw Error('STOP: change overview source differs. No files changed.');source=source.replace(patch.before,patch.after);}
 }
 prepared.push({file,source});
}
if(process.argv[2]==='--apply')for(const item of prepared)await fs.writeFile(item.file,item.source);
console.log('Checked: activity timeline below linked incidents. Frontend only.');
