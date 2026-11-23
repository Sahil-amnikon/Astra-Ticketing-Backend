"""Compile deployment scripts without executing them or accessing application data."""
from pathlib import Path
import re

folder = Path(__file__).resolve().parent
script = (folder / 'deploy-problem-ticket-links.sh').read_text(encoding='utf-8-sig')
blocks = re.findall(r"^python3[^\n]*<<'PY'\r?\n(.*?)^PY\s*$", script, re.MULTILINE | re.DOTALL)
if len(blocks) != 4:
    raise SystemExit('STOP: deployment Python block structure differs. No deployment performed.')
for index, block in enumerate(blocks, 1):
    compile(block, f'deploy-problem-ticket-links.sh:python-block-{index}', 'exec')
compile((folder / 'verify-change-lifecycle-startup.py').read_text(encoding='utf-8-sig'), 'verify-change-lifecycle-startup.py', 'exec')
compile((folder / 'verify-change-request-routing-startup.py').read_text(encoding='utf-8-sig'), 'verify-change-request-routing-startup.py', 'exec')
compile((folder / 'verify-problem-ticket-startup.py').read_text(encoding='utf-8-sig'), 'verify-problem-ticket-startup.py', 'exec')
print('PASS: all four embedded Python blocks and lifecycle preflight compile. No deployment or database changes performed.')
