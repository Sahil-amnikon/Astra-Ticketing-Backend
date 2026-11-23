// Source-only policy update. No database changes, credential reads or backups.
import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

if (!['--apply','--test-workspace-lists'].includes(process.argv[2])) throw new Error('Use --apply for the reviewed Manager full-access policy.');
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const backend = path.join(root, 'ASTRA-Backend-latest');
const frontend = path.join(root, 'ASTRA-Frontend-latest');
const { default: ts } = await import(pathToFileURL(path.join(frontend, 'node_modules/typescript/lib/typescript.js')));
function grantManagerWorkspaceAccess(text) {
  const source = ts.createSourceFile('service-desk.tsx', text, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);
  if (source.parseDiagnostics.length) throw new Error('STOP: the workspace source cannot be parsed. No files written.');
  const found = new Set();
  const insertions = [];
  function visit(node) {
    if (ts.isObjectLiteralExpression(node)) {
      const props = new Map(node.properties.filter(ts.isPropertyAssignment).map(p => [p.name?.text, p.initializer]));
      const href = props.get('href');
      if (href && ts.isStringLiteralLike(href) && ['/client','/admin'].includes(href.text) && props.has('roles')) {
        let array = props.get('roles');
        while (ts.isAsExpression(array) || ts.isSatisfiesExpression(array)) array = array.expression;
        if (!ts.isArrayLiteralExpression(array) || array.elements.some(e => !ts.isStringLiteralLike(e)))
          throw new Error('STOP: a workspace uses dynamic role rules. No files written.');
        found.add(href.text);
        if (!array.elements.some(e => e.text === 'MANAGER')) {
          const prefix = text.slice(array.getStart(source) + 1, array.end - 1).trimEnd();
          const separator = array.elements.length && !prefix.endsWith(',') ? ', ' : ' ';
          insertions.push({ at: array.end - 1, text: separator + "'MANAGER'" });
        }
      }
    }
    ts.forEachChild(node, visit);
  }
  visit(source);
  if (!found.has('/client') || !found.has('/admin'))
    throw new Error('STOP: requester or administration workspace definition was not found. No files written.');
  for (const insertion of insertions.sort((a,b) => b.at-a.at))
    text = text.slice(0,insertion.at)+insertion.text+text.slice(insertion.at);
  return text;
}
if (process.argv[2] === '--test-workspace-lists') {
  const { default: assert } = await import('node:assert/strict');
  const fixtures = [
    "const workspaces=[{href:'/client',roles:['USER','ADMIN']},{href:'/admin',roles:['ADMIN']}];",
    'const cards=[{href:"/client",roles: ["USER", "INTERN", "TECHNICIAN", "ADMIN",] as const},{href:"/admin",roles: [ "ADMIN", "MANAGER" ]}];',
    "const cards=[{href:'/client',roles:[\n'USER',\n'ADMIN',\n]},{href:'/admin',roles:['ADMIN']}];"
  ];
  for (const fixture of fixtures) {
    const updated = grantManagerWorkspaceAccess(fixture);
    assert.equal(grantManagerWorkspaceAccess(updated), updated);
    const parsed=ts.createSourceFile('fixture.tsx',updated,ts.ScriptTarget.Latest,true,ts.ScriptKind.TSX);
    assert.equal(parsed.parseDiagnostics.length,0);
    assert.ok(updated.includes('USER'));
    assert.ok(updated.includes('ADMIN'));
  }
  assert.ok(grantManagerWorkspaceAccess(fixtures[1]).includes('TECHNICIAN'));
  assert.throws(()=>grantManagerWorkspaceAccess("const cards=[{href:'/admin',roles:allowed}];"),/dynamic role/);
  assert.throws(()=>grantManagerWorkspaceAccess("const cards=[];"),/was not found/);
  console.log('PASS: existing extended role lists, quote/spacing variants, trailing commas and repeat runs preserve current workspace roles.');
  process.exit(0);
}
const updates = new Map();
async function read(file) { return (await fs.readFile(file, 'utf8')).replace(/\r\n/g, '\n'); }
function change(text, before, after, label) {
  if (text.includes(before)) return text.replace(before, after);
  if (text.includes(after)) return text;
  throw new Error(`STOP: ${label} differs from the reviewed source. No files written.`);
}
const csHelper = `public static class RoleAccess
{
    public static bool IsFullAccessRole(string? role) => role is "ADMIN" or "MANAGER";
    public static bool HasFullAccess(IEnumerable<string> roles) => roles.Any(IsFullAccessRole);
    // Keep the person's real role label while granting the same capabilities.
    public static string WorkspaceRole(IEnumerable<string> roles) => roles.Contains("ADMIN", StringComparer.Ordinal) ? "ADMIN" : "MANAGER";
}
`;
updates.set(path.join(backend, 'RoleAccess.cs'), csHelper);
const csFiles = (await fs.readdir(backend)).filter(name => name.endsWith('.cs') && name !== 'RoleAccess.cs');
csFiles.push('Database/MySqlStore.Features.cs');
for (const name of csFiles) {
  const file = path.join(backend, name);
  const original = await read(file);
  let text = original;
  // Expand only authorization predicates, never stored role values or role names.
  text = text.replace(/\b((?:\w+\.)?role_code)\s*=\s*'ADMIN'/g, "$1 IN ('ADMIN','MANAGER')");
  text = text.replace(/(\b(?:\w+\.)?role_code\s+IN\s*\()([^)]*)(\))/g, (all, start, values, end) =>
    values.includes("'ADMIN'") && !values.includes("'MANAGER'") ? start + "'MANAGER'," + values + end : all);
  text = text.replace(/\b(\w+)\.Contains\("ADMIN"(?:,\s*StringComparer\.Ordinal)?\)/g, 'RoleAccess.HasFullAccess($1)');
  text = text.replace(/\b(role|code)\s*==\s*"ADMIN"/g, 'RoleAccess.IsFullAccessRole($1)');
  text = text.replace(/\brole\s*!=\s*"ADMIN"/g, '!RoleAccess.IsFullAccessRole(role)');
  if (name === 'FeatureHelp.cs') text = text.replace('roles.Any(x => Equals(x["role_code"], "ADMIN"))', 'roles.Any(x => RoleAccess.IsFullAccessRole(Convert.ToString(x["role_code"])))');
  if (name === 'Program.cs') {
    text = change(text,
      'if(ConnectionString is null)return false;\n        await using var connection=CreateConnection();await connection.OpenAsync(ct);\n        var roleAllowed=false;',
      'if(ConnectionString is null)return false;\n        if(await IsAdministrator(userId,ct))return true;\n        await using var connection=CreateConnection();await connection.OpenAsync(ct);\n        var roleAllowed=false;', 'permission bypass for full-access roles');
    text = change(text,
      '"MANAGER" => ["TICKETS_TEAM_VIEW","TICKETS_ASSIGN","TICKETS_APPROVE","CATEGORY_MANAGE","TAGS_MANAGE","SLA_MANAGE","REPORTS_VIEW","AUDIT_VIEW"],',
      '"MANAGER" => PolicyPermissionCodes,', 'Manager defaults');
    text = change(text, '"MANAGER" or "TEAM_LEAD" => [..StaffPolicyCodes,"AUDIT_VIEW"],', '"MANAGER" => PolicyPermissionCodes,\n        "TEAM_LEAD" => [..StaffPolicyCodes,"AUDIT_VIEW"],', 'Manager permission ceiling');
    text = change(text, 'editable = code is "TECHNICIAN" or "TEAM_LEAD" or "MANAGER"', 'editable = code is "TECHNICIAN" or "TEAM_LEAD"', 'fixed full-access policy');
    text = change(text, 'target is not ("TECHNICIAN" or "TEAM_LEAD" or "MANAGER")', 'target is not ("TECHNICIAN" or "TEAM_LEAD")', 'fixed policy validation');
    text = change(text, 'role is "ADMIN" or "USER" or "INTERN" || AvailableRolePolicy(role).Length == 0', 'role is "ADMIN" or "MANAGER" or "USER" or "INTERN" || AvailableRolePolicy(role).Length == 0', 'fixed user policy validation');
    text = change(text, 'roles = admin ? ["ADMIN"]', 'roles = admin ? [RoleAccess.WorkspaceRole(rawRoles)]', 'management role label');
    // The same assignment exists in both management and technician workspaces.
    text = text.replaceAll('roles = admin ? ["ADMIN"]', 'roles = admin ? [RoleAccess.WorkspaceRole(rawRoles)]');
    text = change(text,
      'workspace == "default" ? admin ? "operations"',
      'workspace == "default" ? admin ? (RoleAccess.WorkspaceRole(rawRoles) == "MANAGER" ? "management" : "operations")', 'default Manager workspace');
    text = change(text,
      'effectiveScope = scope is "INTERNAL" or "BOTH" ? scope : "CLIENT";',
      'effectiveScope = admin ? "ALL" : scope is "INTERNAL" or "BOTH" ? scope : "CLIENT";', 'full internal-request scope');
  }
  if (name === 'FeatureAdminEdits.cs') text = text.replace('All work scope is reserved for administrators.', 'All work scope is reserved for administrators and managers.');
  if (name === 'FeaturePortal.cs') text = change(text,
    'if ((await FQuery("SELECT u.id FROM users u JOIN user_roles r',
    'if (!await IsAdministrator(actor, ct) && (await FQuery("SELECT u.id FROM users u JOIN user_roles r', 'internal portal access');
  if (text !== original) updates.set(file, text);
}
const tsHelper = `export const hasFullAccessRole = (role: string): boolean => role === 'ADMIN' || role === 'MANAGER';
export const hasFullAccess = (roles: readonly string[] | undefined): boolean => roles?.some(hasFullAccessRole) ?? false;
`;
const helperPath = path.join(frontend, 'src/pages/role-access.ts');
updates.set(helperPath, tsHelper);
async function filesUnder(dir) {
  const files = [];
  for (const entry of await fs.readdir(dir, {withFileTypes: true})) {
    const file = path.join(dir, entry.name);
    if (entry.isDirectory()) files.push(...await filesUnder(file));
    else if (/\.tsx?$/.test(entry.name) && file !== helperPath) files.push(file);
  }
  return files;
}
for (const file of await filesUnder(path.join(frontend, 'src'))) {
  const original = await read(file);
  let text = original;
  text = text.replace(/([A-Za-z_$][\w$?.]*)\.includes\('ADMIN'\)/g, 'hasFullAccess($1)');
  text = text.replace(/\b(selectedRole|role\.code)\s*===\s*'ADMIN'/g, 'hasFullAccessRole($1)');
  text = text.replace(/\bselectedRole\s*!==\s*'ADMIN'/g, '!hasFullAccessRole(selectedRole)');
  if (file.endsWith('service-desk.tsx')) {
    text = grantManagerWorkspaceAccess(text);
  }
  if (file.endsWith('record-form.tsx')) text = text.replace(
    'Team Lead access plus approvals, SLA management, complete reporting and audit review.',
    'Full access to all workspaces, MSPs, clients, tickets, people, permissions, configuration, approvals and reports.');
  if (file.endsWith('permission-workspace.tsx')) {
    text = change(text, "['ADMIN', 'USER', 'INTERN'].includes(role)", "['ADMIN', 'MANAGER', 'USER', 'INTERN'].includes(role)", 'fixed Manager user permissions');
    text = change(text, "const effectiveRole = actualRoles.includes('USER')", "const effectiveRole = hasFullAccess(actualRoles)\n    ? actualRoles.find(code => code === 'ADMIN') ? 'ADMIN' : 'MANAGER'\n    : actualRoles.includes('USER')", 'Manager permission display');
    text = change(text, "!denials.includes(code) &&\n    (grants.includes(code) || base.includes(code))", "((mode === 'user' && hasFullAccess(actualRoles)) ||\n      (!denials.includes(code) && (grants.includes(code) || base.includes(code))))", 'fixed full-access display');
  }
  if (text === original) continue;
  const imports = [];
  if (text.includes('hasFullAccess(')) imports.push('hasFullAccess');
  if (text.includes('hasFullAccessRole(')) imports.push('hasFullAccessRole');
  if (imports.length && !text.includes("from './role-access'") && !/from ['"][^'"]*\/role-access['"]/.test(text)) {
    let modulePath = path.relative(path.dirname(file), helperPath).split(path.sep).join('/').replace(/\.ts$/, '');
    if (!modulePath.startsWith('.')) modulePath = './' + modulePath;
    const statement = `import { ${imports.join(', ')} } from '${modulePath}';\n`;
    text = text.startsWith("'use client';") ? text.replace("'use client';", "'use client';\n" + statement) : statement + text;
  }
  updates.set(file, text);
}
for (const [file, text] of updates) {
  let current;
  try { current = await read(file); } catch (e) { if (e.code !== 'ENOENT') throw e; }
  if (current !== text) { await fs.writeFile(file, text); console.log(path.relative(root, file)); }
}
console.log('Manager full-access source policy applied. Role labels preserved. No database changes or backups. Build and deploy before the live service changes.');
