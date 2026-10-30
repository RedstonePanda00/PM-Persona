"""Merge a reviewed native landscape export without reserializing authored records.

Usage: python Source/MissionOne/Apply-Landscape.py artifacts/landscape-TIMESTAMP
The authoring file must still match the pre-generation backup exactly.
"""
from campaign_io import read_bytes, read_text, write_text
import json
import sys
from pathlib import Path

root = Path(__file__).resolve().parents[2]
folder = Path(sys.argv[1]).resolve()
author = root / 'Campaigns/MissionOne.project.json.gz'
baseline = folder / 'before.project.json'
assert read_bytes(author) == read_bytes(baseline), 'Author edited the map since generation; rebase first.'
assert read_text(folder / 'GENERATED.txt').strip() == 'PASS'
read = lambda path: json.loads(read_text(path, encoding='utf-8-sig'))
original, project, generated = read(baseline), read(folder / 'input.project.json'), read(folder / 'generated.project.json')
changes = read(folder / 'changes.json')
assert project['Maps'][0] == original['Maps'][0], 'First mission changed.'
for target in project['Maps'][1:]:
    source = next(m for m in generated['Maps'] if m['Id'] == target['Id'])
    nature = [p for p in source['Objects'] if p['Layer'] in ('InitializedTerrain', 'InitializedNature')]
    authored = target['Objects']
    assert not any(p['Layer'] in ('InitializedTerrain', 'InitializedNature') for p in authored), 'Already initialized.'
    target['Objects'] = nature + authored
    target['NaturalClearings'] = source['NaturalClearings']
    target['Base'] = 'Biome'
    assert len({p['Id'] for p in target['Objects']}) == len(target['Objects'])
    before = next(m for m in original['Maps'] if m['Id'] == target['Id'])
    for old, current in zip(before['Objects'], authored, strict=True):
        expected = dict(old)
        if old['Id'] in changes['factions'] and target['Id'] == 'romulus-subnode':
            expected['Faction'] = 'Hostile'
        assert current == expected, f'Unexpected authoring change: {old["Id"]}'
    print(target['Id'], 'authored', len(authored), 'generated', len(nature))
text = json.dumps(project, ensure_ascii=False, separators=(',', ':'))
write_text(folder / 'final.project.json', text, encoding='utf-8')
# Authoring/runtime publication follows native review, not generation.
print('Staged final.project.json; authoring file remains unchanged.')
