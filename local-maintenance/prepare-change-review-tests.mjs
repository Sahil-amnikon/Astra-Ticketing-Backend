import fs from 'node:fs/promises';import path from 'node:path';import {fileURLToPath} from 'node:url';
const folder=path.dirname(fileURLToPath(import.meta.url));
let test=(await fs.readFile(path.join(folder,'verify-submission-reviews.ps1'),'utf8')).replace(/\r\n/g,'\n');
test=test.replace(/  @'[\s\S]*?'@ \| Set-Content -LiteralPath \(Join-Path \$scratch 'Program.cs'\)/,"  [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'verify-change-review-queue.cs')) | Set-Content -LiteralPath (Join-Path $scratch 'Program.cs')")
 .replaceAll('.submission-check-','.change-review-queue-check-').replaceAll('verify-submission-policy.mjs','verify-change-review-sql.mjs');
await fs.writeFile(path.join(folder,'verify-change-review-queue.ps1'),test);console.log('Prepared local review queue tests.');
