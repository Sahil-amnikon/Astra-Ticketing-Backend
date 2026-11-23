import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { stripTypeScriptTypes } from 'node:module';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const frontend = path.join(root, 'ASTRA-Frontend-latest');
const {hasFullAccess, hasFullAccessRole} = await import(pathToFileURL(path.join(frontend, 'src/pages/role-access.ts')));
assert.equal(hasFullAccess(['MANAGER']), true);
assert.equal(hasFullAccess(['ADMIN']), true);
for (const role of ['TEAM_LEAD', 'TECHNICIAN', 'INTERN', 'USER']) {
  assert.equal(hasFullAccess([role]), false, `${role} must not gain administration access`);
  assert.equal(hasFullAccessRole(role), false);
}
assert.equal(hasFullAccess(undefined), false);
const navigationSource = await fs.readFile(path.join(frontend, 'src/pages/workspace-navigation-data.ts'), 'utf8');
const navigationCode = stripTypeScriptTypes(navigationSource).replace("from './role-access'", `from '${pathToFileURL(path.join(frontend, 'src/pages/role-access.ts')).href}'`);
const navigation = await import('data:text/javascript;base64,' + Buffer.from(navigationCode).toString('base64'));
const manager = navigation.extensionLinks({roles:['MANAGER']}).map(([href]) => href);
const admin = navigation.extensionLinks({roles:['ADMIN']}).map(([href]) => href);
assert.deepEqual(manager, admin);
for (const role of ['TEAM_LEAD', 'TECHNICIAN', 'INTERN', 'USER']) {
  const links = navigation.extensionLinks({roles:[role]}).map(([href])=>href);
  assert.ok(!links.includes('/admin/email'));
  assert.ok(!links.includes('/admin/change-governance'));
}
const program = await fs.readFile(path.join(root, 'ASTRA-Backend-latest/Program.cs'), 'utf8');
assert.match(program, /ur\.role_code IN \('ADMIN','MANAGER'\) AND u\.active=1 AND \(u\.access_expires_at IS NULL/);
assert.match(program, /"MANAGER" => PolicyPermissionCodes/);
assert.match(program, /roles = admin \? \[RoleAccess\.WorkspaceRole\(rawRoles\)\]/);
const desk = await fs.readFile(path.join(frontend, 'src/pages/service-desk.tsx'), 'utf8');
assert.match(desk, /roles: \['ADMIN', 'MANAGER'\]/);
console.log('PASS: Manager and Administrator navigation match; lower roles remain restricted; active-account, expiry, full policy and role-label checks remain present.');
