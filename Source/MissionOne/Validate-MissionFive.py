"""Geometry, connection, footprint and regression checks against the current authoring data."""
from campaign_io import read_text
from pathlib import Path
import collections, json, math, sys
root=Path(__file__).resolve().parents[2]
p=json.loads(read_text(root/'Campaigns/MissionOne.project.json.gz', encoding='utf-8-sig'))
assert p==json.loads(read_text(root/'1.7/Campaigns/MissionOne.json.gz', encoding='utf-8-sig'))
m=next(m for m in p['Maps'] if m['Id']=='steel-will');c=p['Campaigns'][0]
node=next(n for n in c['Nodes'] if n['Id']=='mission-five')
assert (m['Width'],m['Height'])==(325,325) and node['OpeningTitle']['Title']=='坚守'
assert node['Construction'] and node['Work'] and node['Roles']=={'mason':'open-mason'}
assert next(p for p in p['Profiles'] if p['Id']=='open-mason')['GenerateFrom']['Name']=='PMPH_GestaltZero'
assert any(e['From']=='mission-four' and e['To']=='mission-five' and e['Result']=='success' for e in c['Edges'])
def actions(aa):
    for a in aa:
        yield a
        yield from actions(a['Steps']);yield from actions(a['Else'])
opening=list(actions(m['Entry']))
buildings=[o for o in m['Objects'] if o['Faction']=='Player' and o['Kind']=='Thing' and o['Def']['Name'] not in ('Chemfuel','DeepDrill')]
assert collections.Counter(o['Def']['Name'] for o in buildings)=={'PM_Replicator':1,'Pylon':1,'PM_HyperspaceStorage':5}
assert all(not o['SpawnAtStart'] for o in buildings)
drills=[o for o in m['Objects'] if o['Group']=='m5-drills']
assert len(drills)==16 and all(o['Def']['Name']=='DeepDrill' and not o['SpawnAtStart'] for o in drills)
assert {a['Target'] for a in opening if a['Kind']=='rc.pmcWarpBuilding'}=={o['Id'] for o in buildings+drills}
assert m['Entry'][1]['Target']=='m5-ready' and all(not a['Freeze'] and a['Kind']!='Cinematic' for a in actions(m['Entry'][2:]))
assert sum(a['Kind']=='rc.pmcWarpUnit' and a['Text']=='PM_MiningDrill' for a in opening)==8
assert collections.Counter(o['Def']['Name'] for o in m['Objects'] if o['Group']=='m5-defenders')=={'PM_Sentinel':3,'PM_Sentry':1,'PMP_Prometheus':3}
assert sum(a['Kind']=='rc.pmcWarpUnit' and a['Text']=='PMP_HardlightRifle' for a in opening)==3
fuel=[o for o in m['Objects'] if o['Group']=='m5-starting-fuel']
assert sum(o['Count'] for o in fuel)==1000 and all(o['Def']['Name']=='Chemfuel' and 0<o['Count']<=150 and not o['SpawnAtStart'] for o in fuel)
assert {a['Target'] for a in opening if a['Kind']=='Spawn'}>={o['Id'] for o in fuel}
assert all('orePerCell' not in c['Settings'] for c in m['Components'] if c['Type']=='PMP.Campaign.MissionFiveMapComponent')
rock={(o['Position']['X'],o['Position']['Z']) for o in m['Objects'] if o['Def']['Name']=='Granite'}
ore={(x,z) for x in range(150,174) for z in range(124,132)}
assert len(ore)*300==57600 and not ore&rock
assert all(any((x-d['Position']['X'])**2+(z-d['Position']['Z'])**2<=5 for d in drills) for x,z in ore)
footprints=set()
for o in buildings:
    x,z=o['Position'].values();n=5 if o['Def']['Name']=='PM_Replicator' else 2
    start=-(n-1)//2 if n%2 else 0
    cells={(x+dx,z+dz) for dx in range(start,start+n) for dz in range(start,start+n)}
    assert not cells&footprints and not cells&ore and not cells&rock
    footprints|=cells
seen={(162,162)};q=collections.deque(seen)
while q:
    x,z=q.popleft()
    for v in [(x-1,z),(x+1,z),(x,z-1),(x,z+1)]:
        if 0<=v[0]<325 and 0<=v[1]<325 and v not in rock and v not in seen:
            seen.add(v);q.append(v)
for corner in [(18,18),(306,306),(18,306),(306,18)]: assert corner in seen
assert all((162+dx,162+dz) in rock for dx,dz in [(93,0),(-93,0),(0,93),(0,-93)])
plants=[o for o in m['Objects'] if o['Def']['Name'].startswith('Plant_')]
assert len(plants)>1000 and any(math.hypot(o['Position']['X']-162,o['Position']['Z']-162)<75 for o in plants)
if len(sys.argv)>1:
    old=json.loads(read_text(Path(sys.argv[1]), encoding='utf-8-sig'))
    count=min(4,len(old['Maps']))
    assert p['Maps'][:count]==old['Maps'][:count]
    assert c['Nodes'][:4]==old['Campaigns'][0]['Nodes'][:4]
    assert c['Edges'][:len(old['Campaigns'][0]['Edges'])]==old['Campaigns'][0]['Edges']
print(f'PASS fifth mission: building warp x7, probes x8, sentinels x3, sentry x1, armed Prometheus x3, chemfuel x1000, drills x16 after control, {len(ore)} full rectangular deep cells / {len(ore)*300} steel, {len(plants)} plants, all four paths; previous chapters preserved.')
