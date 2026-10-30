"""Add defenses to the user's saved Oct 24 layout without rebuilding either map.

The exact input and native definition catalog are retained in the repair artifacts.
Refuse changed input; repeat runs are safe and reproduce the same IDs.
"""
from campaign_io import read_bytes, read_text, write_text
import collections, copy, json, hashlib
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
ART = ROOT / 'artifacts/editor-map-repair'
SOURCE = ROOT / 'Campaigns/MissionOne.project.json.gz'
BASE = ART / 'baseline/MissionOne.project.json'
CATALOG = ART / 'fixed-20261024-210414/EditorMapRepairAudit/defs.json'

def read(path): return json.loads(read_text(path, encoding='utf-8-sig'))
project = read(BASE)
current = read(SOURCE)
previous_report=ART/'map-change-report.json'
assert current == project or (previous_report.exists() and hashlib.sha256(read_bytes(SOURCE)).hexdigest()==read(previous_report).get('OutputSHA256')), 'Saved project changed; review before applying.'
m = next(m for m in project['Maps'] if m['Id'] == 'romulus-sanctuary')
old = next(v for v in read(ART / 'baseline/MissionOne.runtime.json')['Maps'] if v['Id'] == m['Id'])
old_ids = {o['Id'] for o in old['Objects']}
defs = read(CATALOG)
prefix = 'm3-defense-20261024-'
changes = collections.Counter()
for o in m['Objects']:
    if o['Id'] in old_ids or o['Kind'] not in ('Thing', 'PawnKind', 'Pawn', 'Boss'): continue
    if o['Faction'] != 'Player': continue
    o['Faction'] = 'Empire' if o['Def']['Name'].startswith('Ancient') else 'Hostile'
    changes[o['Faction']] += 1
assert dict(changes) == {'Hostile': 1636, 'Empire': 12}, changes

def cells(o):
    x, z = o['Position']['X'], o['Position']['Z']
    sx, sz = (defs[o['Def']['Name']]['X'], defs[o['Def']['Name']]['Z']) if o['Kind'] == 'Thing' else (1, 1)
    rot = o['Rotation']
    # GenAdj's even-size anchor adjustment. Native validation checks every added footprint too.
    if rot in (1, 3): sx, sz = sz, sx
    if rot in (1, 2) and sz % 2 == 0: z -= 1
    if rot in (2, 3) and sx % 2 == 0: x -= 1
    return {(xx, zz) for xx in range(x-(sx-1)//2, x-(sx-1)//2+sx) for zz in range(z-(sz-1)//2, z-(sz-1)//2+sz)}

occupied = {}
for o in m['Objects']:
    if o['Kind'] not in ('Thing','PawnKind','Pawn','Boss'): continue
    if o['Def']['Name'] == 'HiddenConduit' or o['Layer'] == 'InitializedNature': continue
    # Include future empire construction sites as reserved, even before they spawn.
    for cell in cells(o): occupied.setdefault(cell, []).append(o['Id'])

template = next(o for o in m['Objects'] if o['Def']['Name'] == 'PMP_M3Believer')
added = []
def add(name, x, z, label, group='', kind='Thing', stuff=None):
    o = copy.deepcopy(template)
    o.update(Id=prefix+label, Name=label, Notes='', RelatedObjects=[], Kind=kind,
             Def={'Kind':'PawnKindDef' if kind=='PawnKind' else 'ThingDef','Name':name,'Package':defs.get(name,{}).get('Package','redstone.pmpersona')},
             Position={'X':x,'Z':z}, Rotation=0, Faction='Hostile', Group=group,
             Layer='Auxiliary' if kind=='Marker' else 'Buildings', Stuff=stuff, SpawnAtStart=True)
    if kind != 'Marker':
        footprint = cells(o)
        overlaps = {str(c):occupied[c] for c in footprint if c in occupied}
        assert not overlaps, (label, overlaps)
        assert all(0<=x<m['Width'] and 0<=z<m['Height'] for x,z in footprint)
        for c in footprint: occupied[c]=[o['Id']]
    m['Objects'].append(o);added.append(o)
    return o

believer = 'PMP_M3Believer'
charge = 'Turret_AutoChargeBlaster'
gunner = ['Mech_CentipedeBlaster','Mech_CentipedeGunner']
posts = [('northwest-south',300,160,12),('central-south',327,141,25),('southern-rear',417,103,24),('eastern-rear',468,147,24),('entrance',237,123,9),('control-approach',243,153,9)]
groups = [prefix+'garrison-'+p[0] for p in posts]

# Four open corner emplacements: keep the central east/west and north/south gaps.
for i,(x,z) in enumerate([(295,156),(302,156),(295,164),(302,164)]):
    add(charge,x,z,f'south-corner-turret-{i}',groups[0])
    for j,(gx,gz) in enumerate([(x,z-1 if z==156 else z+2),(x+2,z)]):
        add(believer,gx,gz,f'south-corner-guard-{i}-{j}',groups[0],'PawnKind')

# The user's 4x4 sandbag boxes contain exactly a 2x2 auto-charge turret.
for post,(x,zs) in enumerate([(337,[131,136,141,147,153,158,163]),(426,[86,91,96,102,108,113,118]),(477,[130,135,140,146,152,157,162])],1):
    for i,z in enumerate(zs):
        add(charge,x,z,f'south-box-{post}-turret-{i}',groups[post])
        for j,gz in enumerate([z,z+1]): add(believer,x-3,gz,f'south-box-{post}-guard-{i}-{j}',groups[post],'PawnKind')

# Front C-shaped sandbag positions receive their own turret and three defenders.
for post,points in [(1,[(318,120),(318,137),(318,158)]),(2,[(407,92),(407,112)]),(3,[(458,136),(458,156)])]:
    for i,(x,z) in enumerate(points):
        add(charge,x,z,f'south-front-{post}-turret-{i}',groups[post])
        for j,(gx,gz) in enumerate([(x,z-2),(x,z+3),(x+3,z+1)]): add(believer,gx,gz,f'south-front-{post}-guard-{i}-{j}',groups[post],'PawnKind')

for post,points in enumerate([[(299,156),(299,165)],[(326,132),(326,159)],[(416,91),(416,115)],[(467,134),(467,158)],[(239,121)],[(246,153)]]):
    for i,(x,z) in enumerate(points): add(gunner[(i+post)%2],x,z,f'south-centipede-{post}-{i}',groups[post],'PawnKind')

# Separate defense groups keep Mason's existing task targets and route unchanged.
defend_template = next(a for a in m['Entry'] if a['Kind']=='rc.defendGroup')
for i,(label,x,z,radius) in enumerate(posts):
    marker=add('',x,z,'post-'+label,kind='Marker')
    marker['Def']={'Kind':'ThingDef','Name':'','Package':'ludeon.rimworld'}
    action=copy.deepcopy(defend_template)
    action.update(Id=prefix+'defend-'+label,Name='南部哨站驻守 / Southern garrison',Target=groups[i],Text=marker['Id'],Value=radius)
    m['Entry'].insert(i,action)

# Six spear batteries per northern main position, west-facing C-shaped cover.
# Stay clear of the empire advance lane and its future construction footprints.
sandbag = next(o for o in m['Objects'] if o['Def']['Name']=='Sandbags')
for sector,(left,right) in enumerate([(245,287),(335,377),(425,467)],1):
    for i,(x,z) in enumerate((x,z) for x in (left,right) for z in (206,252,286)):
        add('PMP_RomulusSpearTurret',x,z,f'north-{sector}-turret-{i}',f'sector-{sector}-front')
        cover={(x-2,zz) for zz in range(z-2,z+3)}|{(xx,z-2) for xx in range(x-1,x+3)}|{(xx,z+2) for xx in range(x-1,x+3)}
        for j,(sx,sz) in enumerate(sorted(cover)): add('Sandbags',sx,sz,f'north-{sector}-cover-{i}-{j}',stuff=sandbag['Stuff'])

# New occupied cells clear only generated nature, preserving all authored layout.
new_cells=set().union(*(cells(o) for o in added if o['Kind']!='Marker'))
removed_nature=[o['Id'] for o in m['Objects'] if o['Layer']=='InitializedNature' and (o['Position']['X'],o['Position']['Z']) in new_cells]
m['Objects']=[o for o in m['Objects'] if o['Id'] not in set(removed_nature)]
assert current['Maps'][:2] == project['Maps'][:2], 'First and second maps must remain unchanged'
counts=collections.Counter(o['Def']['Name'] for o in added if o['Kind']!='Marker')
report={'FactionCorrections':dict(changes),'Added':dict(counts),'RemovedGeneratedNature':removed_nature,'AddedIds':[o['Id'] for o in added], 'BaselineSHA256':hashlib.sha256(read_bytes(BASE)).hexdigest()}
for path in [SOURCE,ROOT/'1.7/Campaigns/MissionOne.json.gz']:
    temporary=path.with_suffix(path.suffix+'.tmp')
    write_text(temporary, json.dumps(project,ensure_ascii=False,separators=(',',':')),encoding='utf-8')
    temporary.replace(path)
report['OutputSHA256']=hashlib.sha256(read_bytes(SOURCE)).hexdigest()
write_text(ART/'map-change-report.json', json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps({k:v for k,v in report.items() if k not in ('AddedIds','RemovedGeneratedNature')},ensure_ascii=False,indent=2))
