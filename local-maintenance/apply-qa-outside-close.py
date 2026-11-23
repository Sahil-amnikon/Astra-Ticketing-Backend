from pathlib import Path
import base64,json,os,re,subprocess,sys,tempfile
ROOT=Path.home()
FRONT=ROOT/'ASTRA-Frontend-latest'
BACK=ROOT/'ASTRA-Backend-latest'
changes=json.loads(base64.b64decode('W3sicGF0aCI6IkFTVFJBLUZyb250ZW5kLWxhdGVzdC9zcmMvcGFnZXMvdGlja2V0LXFhLWNoZWNrLnRzeCIsIm9sZCI6IiAgY29uc3QgZGlhbG9nID0gdXNlUmVmPEhUTUxEaWFsb2dFbGVtZW50PihudWxsKTsiLCJuZXdUZXh0IjoiICBjb25zdCBkaWFsb2cgPSB1c2VSZWY8SFRNTERpYWxvZ0VsZW1lbnQ+KG51bGwpO1xuICBjb25zdCBiYWNrZHJvcFByZXNzID0gdXNlUmVmKGZhbHNlKTsiLCJhbGwiOmZhbHNlfSx7InBhdGgiOiJBU1RSQS1Gcm9udGVuZC1sYXRlc3Qvc3JjL3BhZ2VzL3RpY2tldC1xYS1jaGVjay50c3giLCJvbGQiOiIgICAgICBvbkNsb3NlPXsoKSA9PiBzZXRPcGVuKGZhbHNlKX0gb25DYW5jZWw9e2V2ZW50ID0+IHsgaWYgKHNhdmluZykgZXZlbnQucHJldmVudERlZmF1bHQoKTsgfX0+IiwibmV3VGV4dCI6IiAgICAgIG9uQ2xvc2U9eygpID0+IHNldE9wZW4oZmFsc2UpfSBvbkNhbmNlbD17ZXZlbnQgPT4geyBpZiAoc2F2aW5nKSBldmVudC5wcmV2ZW50RGVmYXVsdCgpOyB9fVxuICAgICAgb25Qb2ludGVyRG93bj17ZXZlbnQgPT4ge1xuICAgICAgICBjb25zdCByZWN0ID0gZXZlbnQuY3VycmVudFRhcmdldC5nZXRCb3VuZGluZ0NsaWVudFJlY3QoKTtcbiAgICAgICAgYmFja2Ryb3BQcmVzcy5jdXJyZW50ID0gZXZlbnQudGFyZ2V0ID09PSBldmVudC5jdXJyZW50VGFyZ2V0ICYmXG4gICAgICAgICAgKGV2ZW50LmNsaWVudFggPCByZWN0LmxlZnQgfHwgZXZlbnQuY2xpZW50WCA+IHJlY3QucmlnaHQgfHwgZXZlbnQuY2xpZW50WSA8IHJlY3QudG9wIHx8IGV2ZW50LmNsaWVudFkgPiByZWN0LmJvdHRvbSk7XG4gICAgICB9fVxuICAgICAgb25Qb2ludGVyQ2FuY2VsPXsoKSA9PiB7IGJhY2tkcm9wUHJlc3MuY3VycmVudCA9IGZhbHNlOyB9fVxuICAgICAgb25Qb2ludGVyVXA9e2V2ZW50ID0+IHtcbiAgICAgICAgY29uc3QgcmVjdCA9IGV2ZW50LmN1cnJlbnRUYXJnZXQuZ2V0Qm91bmRpbmdDbGllbnRSZWN0KCk7XG4gICAgICAgIGNvbnN0IG91dHNpZGUgPSBldmVudC50YXJnZXQgPT09IGV2ZW50LmN1cnJlbnRUYXJnZXQgJiZcbiAgICAgICAgICAoZXZlbnQuY2xpZW50WCA8IHJlY3QubGVmdCB8fCBldmVudC5jbGllbnRYID4gcmVjdC5yaWdodCB8fCBldmVudC5jbGllbnRZIDwgcmVjdC50b3AgfHwgZXZlbnQuY2xpZW50WSA+IHJlY3QuYm90dG9tKTtcbiAgICAgICAgaWYgKGJhY2tkcm9wUHJlc3MuY3VycmVudCAmJiBvdXRzaWRlICYmICFzYXZpbmcpIGV2ZW50LmN1cnJlbnRUYXJnZXQuY2xvc2UoKTtcbiAgICAgICAgYmFja2Ryb3BQcmVzcy5jdXJyZW50ID0gZmFsc2U7XG4gICAgICB9fT4iLCJhbGwiOmZhbHNlfV0='))
original={ROOT/c['path']:((ROOT/c['path']).read_text(encoding='utf-8-sig') if (ROOT/c['path']).exists() else None) for c in changes}
updated={p:(s if s is not None else '') for p,s in original.items()}
for change in changes:
    path=ROOT/change['path'];source=updated[path]
    if change['newText'] in source:continue
    if not change['all'] and source.count(change['old'])!=1:raise SystemExit('STOP: source differs: '+change['path']+'. No files changed.')
    if change['old'] not in source:raise SystemExit('STOP: source differs: '+change['path']+'. No files changed.')
    updated[path]=source.replace(change['old'],change['newText']) if change['all'] else source.replace(change['old'],change['newText'],1)
paths=list(updated)
print('Applying QA popup outside-click closing.',flush=True)
if '--apply' not in sys.argv:raise SystemExit('Review only. No source changes.')
try:
    for p in paths:
        if updated[p] != original[p]: p.write_text(updated[p], encoding='utf-8')
    subprocess.run(['npm','run','build'], cwd=FRONT, check=True)
except BaseException:
    for p in paths:
        if updated[p] != original[p]:
            if original[p] is None: p.unlink(missing_ok=True)
            else: p.write_text(original[p], encoding='utf-8')
    print('Update failed; original source restored from memory. No backup files created.', flush=True)
    raise
print('SUCCESS: QA popup outside-click closing built successfully. No review data changed.', flush=True)
