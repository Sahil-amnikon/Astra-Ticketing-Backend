from pathlib import Path
import re
folder = Path(__file__).resolve().parent
script = (folder / 'deploy-service-report-fix.sh').read_text(encoding='utf-8-sig')
blocks = re.findall(r"^python3[^\n]*<<'PY'\r?\n(.*?)^PY\s*$", script, re.MULTILINE | re.DOTALL)
if len(blocks) != 3:
    raise SystemExit('STOP: backend deployment block structure differs.')
for index, block in enumerate(blocks, 1):
    compile(block, f'deploy-service-report-fix.sh:python-block-{index}', 'exec')
compile((folder / 'verify-service-report-startup.py').read_text(encoding='utf-8-sig'), 'verify-service-report-startup.py', 'exec')
print('PASS: backend-only deployment blocks and report preflight compile. No data accessed.')
