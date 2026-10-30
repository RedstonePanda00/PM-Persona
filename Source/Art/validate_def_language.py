"""Ensure Def XML uses English defaults and Chinese is kept in localization files."""
from pathlib import Path
import re
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
errors = []
files = list((ROOT / '1.7/Defs').rglob('*.xml'))
for path in files:
    ET.parse(path)
    for line, text in enumerate(path.read_text(encoding='utf-8-sig').splitlines(), 1):
        if re.search(r'[\u3400-\u9fff]', text):
            errors.append(f'{path.relative_to(ROOT)}:{line}: Chinese text in Def XML')
if errors:
    raise SystemExit('\n'.join(errors))
print(f'PASS: {len(files)} Def XML files parse and contain no Chinese text.')
