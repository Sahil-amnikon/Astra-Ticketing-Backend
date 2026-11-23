from pathlib import Path
import base64, json, os, re, subprocess, sys, tempfile

ROOT = Path.home()
FRONT = ROOT / 'ASTRA-Frontend-latest'
BACK = ROOT / 'ASTRA-Backend-latest'
SECTION = json.loads(base64.b64decode('IiAgICAgICAgPHNlY3Rpb24+XG4gICAgICAgICAgPGgyPlJlcXVlc3RlciBhbmQgY2xpZW50PC9oMj5cbiAgICAgICAgICA8ZGl2IGNsYXNzTmFtZT1cImZvcm0tZ3JpZFwiPlxuICAgICAgICAgICAgPFNlbGVjdFxuICAgICAgICAgICAgICBsYWJlbD1cIk1TUFwiXG4gICAgICAgICAgICAgIHJlcXVpcmVkXG4gICAgICAgICAgICAgIHZhbHVlPXtjbGllbnRJZH1cbiAgICAgICAgICAgICAgb25DaGFuZ2U9eyh2YWx1ZSkgPT4ge1xuICAgICAgICAgICAgICAgIHNldENsaWVudElkKHZhbHVlKTtcbiAgICAgICAgICAgICAgICBmaWVsZCgncmVxdWVzdGVySWQnLCAnJyk7XG4gICAgICAgICAgICAgICAgZmllbGQoJ3NpdGVJZCcsICcnKTtcbiAgICAgICAgICAgICAgICBmaWVsZCgnYXNzaWduZWRVc2VySWQnLCAnJyk7XG4gICAgICAgICAgICAgICAgZmllbGQoJ2RlcGFydG1lbnRJZCcsICcnKTtcbiAgICAgICAgICAgICAgICBmaWVsZCgnbG9jYXRpb25UZXh0JywgJycpO1xuICAgICAgICAgICAgICB9fVxuICAgICAgICAgICAgICBvcHRpb25zPXtvcHRpb25zLmNsaWVudHN9XG4gICAgICAgICAgICAvPlxuICAgICAgICAgICAgICA8U2VsZWN0XG4gICAgICAgICAgICAgICAgbGFiZWw9XCJDbGllbnRcIlxuICAgICAgICAgICAgICAgIHZhbHVlPXtmb3JtLnNpdGVJZH1cbiAgICAgICAgICAgICAgICBvbkNoYW5nZT17KHZhbHVlKSA9PiB7XG4gICAgICAgICAgICAgICAgICBmaWVsZCgnc2l0ZUlkJywgdmFsdWUpO1xuICAgICAgICAgICAgICAgICAgZmllbGQoJ3JlcXVlc3RlcklkJywgJycpO1xuICAgICAgICAgICAgICAgICAgZmllbGQoJ2Fzc2lnbmVkVXNlcklkJywgJycpO1xuICAgICAgICAgICAgICAgIH19XG4gICAgICAgICAgICAgICAgb3B0aW9ucz17c2l0ZXN9XG4gICAgICAgICAgICAgICAgcmVxdWlyZWRcbiAgICAgICAgICAgICAgICBkaXNhYmxlZD17IWNsaWVudElkfVxuICAgICAgICAgICAgICAvPlxuICAgICAgICAgICAgPFNlbGVjdFxuICAgICAgICAgICAgICBsYWJlbD1cIlVzZXJcIlxuICAgICAgICAgICAgICB2YWx1ZT17Zm9ybS5yZXF1ZXN0ZXJJZH1cbiAgICAgICAgICAgICAgb25DaGFuZ2U9eyh2YWx1ZSkgPT4gZmllbGQoJ3JlcXVlc3RlcklkJywgdmFsdWUpfVxuICAgICAgICAgICAgICBvcHRpb25zPXtjb250YWN0c31cbiAgICAgICAgICAgICAgcmVxdWlyZWRcbiAgICAgICAgICAgICAgZGlzYWJsZWQ9eyFmb3JtLnNpdGVJZCB8fCBjb250YWN0cy5sZW5ndGggPT09IDB9XG4gICAgICAgICAgICAvPlxuICAgICAgICAgICAgPFNlbGVjdFxuICAgICAgICAgICAgICBsYWJlbD1cIlRlY2huaWNpYW5cIlxuICAgICAgICAgICAgICB2YWx1ZT17c2VsZWN0ZWRBc3NpZ25lZX1cbiAgICAgICAgICAgICAgb25DaGFuZ2U9eyh2YWx1ZSkgPT4gZmllbGQoJ2Fzc2lnbmVkVXNlcklkJywgdmFsdWUpfVxuICAgICAgICAgICAgICBvcHRpb25zPXthc3NpZ25lZXMuaXRlbXN9XG4gICAgICAgICAgICAgIGRpc2FibGVkPXshY2FuQXNzaWduIHx8ICFjbGllbnRJZCB8fCBhc3NpZ25lZXMubG9hZGluZyB8fCAhIWFzc2lnbmVlcy5lcnJvcn1cbiAgICAgICAgICAgICAgb3B0aW9uYWxcbiAgICAgICAgICAgIC8+XG4gICAgICAgICAgICB7Zm9ybS5zaXRlSWQgJiYgY29udGFjdHMubGVuZ3RoID09PSAwICYmIChcbiAgICAgICAgICAgICAgPHAgY2xhc3NOYW1lPVwiZmllbGQtaGVscCBzcGFuLTJcIj5ObyB1c2VycyBoYXZlIGJlZW4gYWRkZWQgdG8gdGhpcyBjbGllbnQgeWV0LiBBZGQgYSBjbGllbnQgdXNlciBiZWZvcmUgY3JlYXRpbmcgYSB0aWNrZXQuPC9wPlxuICAgICAgICAgICAgKX1cbiAgICAgICAgICAgIHthc3NpZ25lZXMuZXJyb3IgJiYgPHAgcm9sZT1cImFsZXJ0XCIgY2xhc3NOYW1lPVwiZmllbGQtaGVscCBzcGFuLTJcIj57YXNzaWduZWVzLmVycm9yfTwvcD59XG4gICAgICAgICAgICB7Y2xpZW50SWQgPT09ICcwMDAwMDAwMC0wMDAwLTQwMDAtODAwMC0wMDAwMDAwMDAxMzInID8gKFxuICAgICAgICAgICAgICA8PlxuICAgICAgICAgICAgICAgIDxTZWxlY3RcbiAgICAgICAgICAgICAgICAgIGxhYmVsPVwiSW50ZXJuYWwgZGVwYXJ0bWVudFwiXG4gICAgICAgICAgICAgICAgICB2YWx1ZT17Zm9ybS5kZXBhcnRtZW50SWR9XG4gICAgICAgICAgICAgICAgICBvbkNoYW5nZT17KHZhbHVlKSA9PiBmaWVsZCgnZGVwYXJ0bWVudElkJywgdmFsdWUpfVxuICAgICAgICAgICAgICAgICAgb3B0aW9ucz17b3B0aW9ucy5kZXBhcnRtZW50cyA/PyBbXX1cbiAgICAgICAgICAgICAgICAgIHJlcXVpcmVkXG4gICAgICAgICAgICAgICAgLz5cbiAgICAgICAgICAgICAgICA8bGFiZWw+XG4gICAgICAgICAgICAgICAgICBMb2NhdGlvbiAvIHByb2plY3QgKG9wdGlvbmFsKVxuICAgICAgICAgICAgICAgICAgPGlucHV0XG4gICAgICAgICAgICAgICAgICAgIG1heExlbmd0aD17NTAwfVxuICAgICAgICAgICAgICAgICAgICB2YWx1ZT17Zm9ybS5sb2NhdGlvblRleHR9XG4gICAgICAgICAgICAgICAgICAgIG9uQ2hhbmdlPXsoZXZlbnQpID0+XG4gICAgICAgICAgICAgICAgICAgICAgZmllbGQoJ2xvY2F0aW9uVGV4dCcsIGV2ZW50LnRhcmdldC52YWx1ZSlcbiAgICAgICAgICAgICAgICAgICAgfVxuICAgICAgICAgICAgICAgICAgICBwbGFjZWhvbGRlcj1cIk9mZmljZSwgcm9vbSwgcHJvamVjdCBvciB3b3JrIGFyZWFcIlxuICAgICAgICAgICAgICAgICAgLz5cbiAgICAgICAgICAgICAgICA8L2xhYmVsPlxuICAgICAgICAgICAgICA8Lz5cbiAgICAgICAgICAgICkgOiBudWxsfVxuICAgICAgICAgIDwvZGl2PlxuICAgICAgICA8L3NlY3Rpb24+XG4i').decode())

def replace_once(text, old, new, label):
    if old == new or new in text:
        return text
    if text.count(old) != 1:
        raise SystemExit('STOP: unexpected source for ' + label + '. No files changed.')
    return text.replace(old, new, 1)

paths = [FRONT/'src/pages/ticket-workspace.tsx', BACK/'Program.cs', BACK/'FeatureTicketAssignees.cs']
original = {p: p.read_text(encoding='utf-8-sig') for p in paths}
updated = dict(original)
f = updated[paths[0]]
f = f.replace("  const [requesters, setRequesters] = useState(options.contacts);\n", '')
f = replace_once(f, "    assignedUserId: canAssign ? session.user.id : '',", "    assignedUserId: '',", 'initial assignment')
f = replace_once(f, '  const contacts = requesters.filter((item) => item.client_id === clientId);', '  const contacts = options.contacts.filter((item) => item.client_id === clientId && item.site_id === form.siteId);', 'client users')
if "  const assignees = useTicketAssignees(canAssign ? clientId : '', form.siteId, request);" in f:
    f = replace_once(f, "  const assignees = useTicketAssignees(canAssign ? clientId : '', form.siteId, request);", '  const assignees = useTicketAssignees(clientId, form.siteId, request);', 'MSP assignees')
else:
    f = replace_once(f, "    canAssign ? clientId : '', form.siteId, request, session.user.roles.includes('ADMIN'),", '    clientId, form.siteId, request,', 'MSP assignees')
start = f.find('        <section>\n          <h2>Requester and client</h2>')
end = f.find('        <section>\n          <h2>Ticket details</h2>', start)
if start < 0 or end < 0 or f[start:end].count('<h2>') != 1:
    raise SystemExit('STOP: requester section differs. No files changed.')
f = f[:start] + SECTION + f[end:]
assignment = re.compile(r'            \{canAssign && <>\n            <Select\n              label="Assign to"[\s\S]*?            </>\}\n')
if assignment.search(f):
    f, count = assignment.subn('', f)
    if count != 1: raise SystemExit('STOP: assignment section differs.')
# Fail safely if another old assignment block remains in NewTicket.
new_ticket = f[f.find('function NewTicket('):f.find('async function fileToBase64(')]
if 'label="Assign to"' in new_ticket or 'setRequesters(' in new_ticket:
    raise SystemExit('STOP: unexpected legacy new-ticket fields. No files changed.')
guard = "    if (!form.siteId || !contacts.some((contact) => contact.id === form.requesterId)) {\n      onError('Select a user belonging to the selected client before creating the ticket.');\n      return;\n    }"
f = replace_once(f, '    if (assignees.loading) return;\n    setSaving(true);', '    if (assignees.loading) return;\n' + guard + '\n    setSaving(true);', 'requester validation')
f = replace_once(f, '<button className="primary" disabled={saving || assignees.loading}>', '<button className="primary" disabled={saving || assignees.loading || !form.siteId || !form.requesterId}>', 'submit validation')
updated[paths[0]] = f
b = updated[paths[1]]
b = replace_once(b, 'SELECT x.id,x.client_id,x.name,x.email FROM contacts x WHERE x.active AND', 'SELECT x.id,x.client_id,x.site_id,x.name,x.email FROM contacts x WHERE x.active AND', 'contact client relationship')
b = replace_once(b, 'EXISTS(SELECT 1 FROM contacts WHERE id=@requester AND client_id=@client AND active),EXISTS(SELECT 1 FROM categories', 'EXISTS(SELECT 1 FROM contacts WHERE id=@requester AND client_id=@client AND active AND (@site IS NULL OR site_id=@site)),EXISTS(SELECT 1 FROM categories', 'server requester validation')
updated[paths[1]] = b
a = updated[paths[2]]
# Keep the VM's existing method/property signature and query call sites.
scope_pattern = re.compile(r'((?:private|public)\s+(?:static\s+|const\s+)?string\s+TicketAssigneeScope(?:\([^)]*\))?\s*(?:=>|=)\s*\$?""")[\s\S]*?(""";)')
scope_sql = "\n        ((@client='00000000-0000-4000-8000-000000000132')\n             OR (EXISTS(SELECT 1 FROM client_users cu WHERE cu.user_id=u.id AND cu.client_id=@client)\n                 AND (@site IS NULL OR NOT EXISTS(SELECT 1 FROM user_sites us WHERE us.user_id=u.id)\n                      OR EXISTS(SELECT 1 FROM user_sites us WHERE us.user_id=u.id AND us.site_id=@site))))\n        "
a, scope_count = scope_pattern.subn(lambda match: match[1] + scope_sql + match[2], a)
if scope_count != 1:
    raise SystemExit('STOP: unexpected technician scope definition. No files changed.')
updated[paths[2]] = a
print('Reviewed source update: MSP / Client / User / Technician.', flush=True)
print('No database records or access roles will be changed.', flush=True)
if '--apply' not in sys.argv:
    for p in paths: print(('UPDATE: ' if updated[p] != original[p] else 'CURRENT: ') + str(p.relative_to(ROOT)))
    print('Review only. Use --apply to build and restart the Development preview.')
    raise SystemExit(0)
# Confirm a single owned Development preview before touching source.
listeners = subprocess.check_output(['ss','-ltnp','sport = :5200'], text=True)
pids = set(re.findall(r'pid=(\d+)', listeners))
if len(pids) != 1: raise SystemExit('STOP: expected one existing backend on port 5200.')
proc = Path('/proc') / pids.pop()
if proc.stat().st_uid != os.getuid() or (proc/'cwd').resolve() != BACK.resolve():
    raise SystemExit('STOP: backend owner or project folder differs.')
env = dict(part.split(b'=',1) for part in (proc/'environ').read_bytes().split(b'\0') if b'=' in part)
env = {os.fsdecode(k):os.fsdecode(v) for k,v in env.items()}
if (env.get('DOTNET_ENVIRONMENT') or env.get('ASPNETCORE_ENVIRONMENT')) != 'Development':
    raise SystemExit('STOP: only the Development preview can be updated.')
if env.get('ASPNETCORE_URLS') != 'http://127.0.0.1:5200':
    raise SystemExit('STOP: expected loopback-only preview.')
helper = BACK/'local-maintenance/restart-ticket-preview.py'
if not helper.is_file(): raise SystemExit('STOP: project restart helper is missing.')
try:
    for p in paths:
        if updated[p] != original[p]: p.write_text(updated[p], encoding='utf-8')
    subprocess.run(['npm','run','build'], cwd=FRONT, check=True)
    build = tempfile.mkdtemp(prefix='astra-ticket-ready.', dir='/tmp')
    subprocess.run(['dotnet','build','Astra.Api.csproj','-o',build], cwd=BACK, check=True)
    subprocess.run(['python3',str(helper),build], cwd=BACK, check=True)
except BaseException:
    for p in paths:
        if updated[p] != original[p]: p.write_text(original[p], encoding='utf-8')
    print('Update failed; original source restored from memory. No backup files created.', flush=True)
    raise
print('SUCCESS: Ubuntu Development preview updated. Refresh the New Ticket page.', flush=True)
