"""Validate current third-map footprints against native XML inheritance. Report only."""
from campaign_io import read_text
import json,sys,xml.etree.ElementTree as E
from pathlib import Path
R=Path(__file__).resolve().parents[2]
defs={};names={}
for path in list(Path('C:/Program Files (x86)/Steam/steamapps/common/RimWorld/Data').glob('*/Defs/**/*.xml'))+list((R/'1.7/Defs').rglob('*.xml')):
 try:tree=E.parse(path).getroot()
 except E.ParseError:continue
 for e in tree:
  if e.tag!='ThingDef':continue
  if e.findtext('defName'):defs[e.findtext('defName')]=e
  if e.get('Name'):names[e.get('Name')]=e
def value(e,k):
 v=e.findtext(k)
 if v is None and e.get('ParentName') in names:return value(names[e.get('ParentName')],k)
 return v
def footprint(o):
 e=defs.get(o.get('Def',{}).get('Name',''))
 if e is None:return set()
 sx,sz=[int(x) for x in (value(e,'size') or '(1,1)').strip('()').split(',')]
 if o.get('Rotation',0)%2:sx,sz=sz,sx
 x,z=o['Position']['X'],o['Position']['Z']
 return {(xx,zz) for xx in range(x-(sx-1)//2,x-(sx-1)//2+sx) for zz in range(z-(sz-1)//2,z-(sz-1)//2+sz)}
def buildings(m):return [o for o in m['Objects'] if o['Kind']=='Thing' and o.get('SpawnAtStart',True) and o['Def']['Name'] in defs and value(defs[o['Def']['Name']],'category')=='Building' and o['Def']['Name']!='HiddenConduit']
if __name__=='__main__':
 m=next(m for m in json.loads(read_text(R/'Campaigns/MissionOne.project.json.gz', encoding='utf-8-sig'))['Maps'] if m['Id']=='romulus-sanctuary')
 occupied={};errors=[]
 for o in buildings(m):
  for cell in footprint(o):
   if cell in occupied:errors.append((o['Id'],occupied[cell],cell))
   occupied[cell]=o['Id']
 assert not errors,errors
 print('PASS: no building footprint overlaps.')
 if len(sys.argv)>1:
  before=json.loads(read_text(Path(sys.argv[1]), encoding='utf-8-sig'))
  after=json.loads(read_text(R/'Campaigns/MissionOne.project.json.gz', encoding='utf-8-sig'))
  assert [a for a in before['Maps'] if a['Id']!=m['Id']]==[a for a in after['Maps'] if a['Id']!=m['Id']]
  assert before['Campaigns']==after['Campaigns']
  print('PASS: other maps and campaign progression unchanged against the supplied baseline.')
