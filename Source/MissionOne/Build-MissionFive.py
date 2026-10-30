"""Append the fifth demo once, preserving all existing authored chapters verbatim."""
from campaign_io import read_text, write_text
from pathlib import Path
import copy, datetime, hashlib, json, math, random, shutil

ROOT = Path(__file__).resolve().parents[2]
path = ROOT / 'Campaigns/MissionOne.project.json.gz'
p = json.loads(read_text(path, encoding='utf-8-sig'))
assert not any(m['Id'] == 'steel-will' for m in p['Maps']), 'Edit the existing fifth map; do not regenerate it.'
before = copy.deepcopy(p)
backup = ROOT / 'artifacts/mission-five' / ('baseline-' + datetime.datetime.now().strftime('%Y%m%d-%H%M%S'))
backup.mkdir(parents=True)
for name in ['Campaigns/MissionOne.project.json.gz', '1.7/Campaigns/MissionOne.json.gz', '1.7/Assemblies/PMP.Campaign.dll']:
    shutil.copy2(ROOT / name, backup / Path(name).name)
m = copy.deepcopy(p['Maps'][3])
m.update(Id='steel-will', Name='第五关 · 钢之意志', Notes='行动：坚守。建筑使用 PMC 施工折跃；探机使用单位折跃。325×325 四山口生存防守 Demo。', Width=325, Height=325, Roles=[], Inputs={})
for k in ['Objects','Rules','Entry','Defeat','Groups','Rewards','Objectives','Hud','Regions','CombatPatrols','Interactions','Components','NaturalClearings','PatrolRoutes']:
    m[k] = []
m['Stealth']['Actor'] = ''
m['Terrain'] = dict(Kind='TerrainDef', Name='Soil', Package='ludeon.rimworld')
m['TerrainInitialization'].update(Seed='steel-will-demo', Mountains=0, Vegetation=1, Temperature=21)

def named(id, name=''):
    return dict(Id=id, Name=name or id, Notes='', RelatedObjects=[])
def cond(op='Always', key='', value=0, children=None):
    return dict(Op=op, Key=key, Value=value, Children=children or [])
def act(kind, id, target='', text='', value=0, steps=None, freeze=False):
    return dict(**named(id), Kind=kind, Target=target, Text=text, Value=value, Steps=steps or [], Else=[], When=cond(), Freeze=freeze, Arrival='Ground', Origin='', Communication=None, SoundPulse=None)
def ref(name, kind='ThingDef'):
    package = 'redstonepanda.protossmechcore' if name.startswith('PM_') or name=='Pylon' else 'redstone.pmpersona' if name.startswith('PMP') else 'ludeon.rimworld'
    return dict(Kind=kind, Name=name, Package=package)
template = next(o for o in p['Maps'][3]['Objects'] if o['Kind']=='Marker')
def obj(id, name, x, z, kind='Thing', faction='Neutral', spawn=True, group='', layer=None):
    o = copy.deepcopy(template)
    o.update(Id=id, Name=id, Kind=kind, Def=ref(name, {'PawnKind':'PawnKindDef','Terrain':'TerrainDef','Roof':'RoofDef'}.get(kind,'ThingDef')), Position=dict(X=x,Z=z), Faction=faction, SpawnAtStart=spawn, Group=group, Layer=layer or ('Auxiliary' if kind=='Marker' else 'Terrain' if kind=='Terrain' else 'Objects'))
    m['Objects'].append(o)
    return o
def rule(id, actions, when, seconds=1, once=True, position=(162,162)):
    r = copy.deepcopy(p['Maps'][3]['Rules'][-1])
    r.update(Id=id, Name=id, Notes='', RelatedObjects=[], Host='', Actions=actions, Events=[dict(Kind='Periodic',Target='',Seconds=seconds)], When=when, Once=once, Enabled=True, Position=dict(X=position[0],Z=position[1]), ShowOnMap=True)
    m['Rules'].append(r)
def comm(text, id):
    a = act('Communication',id,text=text,freeze=True)
    a['Communication'] = dict(BackgroundOpacity=.28,CropHeight=1,CropWidth=1,CropX=0,CropY=0,Duration=5,Portrait=ref('PMP_Comms_Price','CommunicationPortraitDef'),PortraitPackage='',PortraitPath='',PortraitSize=128,Renderer='pmp.communication',RendererData={'side':'ally'},RendererPackage='redstone.pmpersona',Speaker='',SpeakerName='普莱斯',Spectrum=dict(BarCount=67,Color='#FFD447',Enabled=True,Height=56,SimulateSpeech=True,Width=340),Width=620,X=16,Y=260)
    return a

# Authored nature remains visible and editable in the map editor.
rng = random.Random(51026)
rocks = set()
for x in range(325):
    for z in range(325):
        dx,dz=x-162,z-162
        r=math.hypot(dx,dz);a=math.atan2(dz,dx)
        inner=78+2*math.sin(a*7)+math.sin(a*13)
        outer=108+3*math.sin(a*5)+1.5*math.sin(a*11)
        # Perpendicular distance to either diagonal: 17-cell-wide through passages.
        lane=abs(abs(dx)-abs(dz))/math.sqrt(2)<8.5
        if inner<=r<=outer and not lane:
            rocks.add((x,z))
            obj(f'm5-rock-{x}-{z}','Granite',x,z)
            obj(f'm5-roof-{x}-{z}','RoofRockThick' if inner+6<r<outer-4 else 'RoofRockThin',x,z,'Roof')
        elif r>24 and not (lane and r>73):
            density=.085 if r>112 else .055
            if rng.random()<density:
                plant=rng.choices(['Plant_Grass','Plant_Bush','Plant_TreeOak','Plant_TreePoplar'],[65,22,9,4])[0]
                obj(f'm5-plant-{x}-{z}',plant,x,z,layer='InitializedNature')
        elif lane and r>73:
            obj(f'm5-path-{x}-{z}','Gravel',x,z,'Terrain')

obj('m5-overview','',162,162,'Marker')
obj('m5-base','',162,160,'Marker')
obj('m5-replicator','PM_Replicator',162,162,faction='Player',spawn=False)
# Pylon field radius is 10: its center is eight cells from the replicator.
obj('m5-pylon','Pylon',162,170,faction='Player',spawn=False)
for i,x in enumerate([150,156,162,168,174]):
    obj(f'm5-storage-{i}','PM_HyperspaceStorage',x,153,faction='Player',spawn=False)
for i,x in enumerate([148,152,156,160,164,168,172,176]):
    obj(f'm5-probe-{i}','PM_Probe',x,143,'PawnKind','Player',False,'m5-probes')

corners=[('sw','左下',18,18,1,1),('ne','右上',306,306,-1,-1),('nw','左上',18,306,1,-1),('se','右下',306,18,-1,1)]
for code,label,x,z,sx,sz in corners:
    marker=obj('m5-spawn-'+code,'',x,z,'Marker');marker['Name']=label+'出兵点'
    for i in range(20):
        # Reserve slots per source; living slots cannot be replaced by later waves.
        kind='PMP_LegionGuard' if i>=15 and i%4==3 else 'PMP_LegionCenturion' if i>=7 and i%4==3 else 'PMP_Warhound' if i%4==2 else 'PMP_M3Believer'
        obj(f'm5-{code}-{i}',kind,x+sx*(i%5)*2,z+sz*(i//5)*2,'PawnKind','Hostile',False,'m5-wave-'+code)

settings={'oreMinX':'150','oreMaxX':'173','oreMinZ':'124','oreMaxZ':'131','holdSeconds':'900','enemyCap':'80'}
m['Components']=[dict(**named('m5-systems','钢之意志：深层矿、开局配置、成绩和撤离'),Type='PMP.Campaign.MissionFiveMapComponent',Enabled=True,Settings=settings)]
m['Objectives']=[dict(**named('m5-hold','坚守至盟友完成防御阵地'),Mode='Standard',Role='',Required=True),dict(**named('m5-extract','完成折跃撤离'),Mode='Standard',Role='',Required=True)]
opening=[act('Camera','m5-wide','m5-overview','FitMap'),act('rc.openingTitle','m5-title'),act('Wait','m5-wide-hold',value=2,freeze=True),act('Camera','m5-close','m5-base','30',3,freeze=True),act('rc.pmcWarpBuilding','m5-pylon-warp','m5-pylon',value=4),act('Parallel','m5-production-warp',steps=[act('rc.pmcWarpBuilding','m5-replicator-warp','m5-replicator',value=5)]+[act('rc.pmcWarpBuilding',f'm5-storage-warp-{i}',f'm5-storage-{i}',value=4) for i in range(5)]),act('Parallel','m5-probes-warp',steps=[act('rc.pmcWarpUnit',f'm5-probe-warp-{i}',f'm5-probe-{i}','PM_MiningDrill',3) for i in range(8)]),act('pmp.m5.ready','m5-configure'),act('Camera','m5-resource-view','m5-base','44',1,freeze=True),comm('前哨已经落地。利用这里的钢铁建立防线，把他们引过来。','m5-line-1'),comm('盟友需要时间建立阵地。守住四个入口，我们会通知你什么时候可以撤离。','m5-line-2')]
m['Entry']=[act('Cinematic','m5-opening',text='RevealMap',steps=opening),act('Set','m5-release','m5-ready',value=1)]

# Exact one-shot wave times; slots/cap defer a blocked wave rather than silently dropping it.
times=[(180,'sw',4),(240,'ne',4),(310,'sw',6),(370,'nw',6),(400,'se',6),(440,'ne',8),(490,'sw',8),(520,'nw',8),(560,'se',8),(600,'ne',10),(640,'sw',10),(670,'nw',10),(700,'se',10),(740,'ne',12),(770,'sw',12),(800,'nw',12),(830,'se',12),(860,'ne',14)]
for i,(second,code,count) in enumerate(times):
    active=cond('And',children=[cond('Equal','m5-ready',1),cond('Equal','m5-ending',0),cond('AtLeast','m5-seconds',second)])
    rule(f'm5-wave-{i:02}',[act('pmp.m5.wave',f'm5-arrival-{i:02}','m5-wave-'+code,text=next(l for c,l,*_ in corners if c==code),value=count)],active,position=next((x,z) for c,l,x,z,*_ in corners if c==code))
for code,label,x,z,*_ in corners:
    rule('m5-endless-'+code,[act('pmp.m5.wave','m5-endless-arrival-'+code,'m5-wave-'+code,text=label,value=16)],cond('And',children=[cond('Equal','m5-ready',1),cond('Equal','m5-ending',0),cond('AtLeast','m5-seconds',900)]),seconds=90,once=False,position=(x,z))
rule('m5-hold-complete',[act('Objective','m5-hold-done','m5-hold','Complete'),act('Message','m5-extraction-available',text='盟友阵地已完成。现在可以撤离，也可以继续坚守。')],cond('AtLeast','m5-seconds',900))
m['Groups']=[dict(**named('m5-extraction','启动群体召回并结算'),Actor='',Actions=[act('Cinematic','m5-ending-scene',steps=[comm('盟友已经站稳脚跟。正在执行群体召回。','m5-end-line'),act('rc.pmcRecall','m5-recall')]),act('pmp.m5.finish','m5-finish')])]

campaign=p['Campaigns'][0]
node=copy.deepcopy(campaign['Nodes'][3]);node.update(Id='mission-five',Name='第五关 · 钢之意志',Map=m['Id'],X=1580,Roles={},Inherit=False,Construction=True,Work=True,Research=True,Storyteller=False,Hunger=False,Mood=False)
node['OpeningTitle'].update(Title='坚守',Lines=['行动单位：前哨建设探机组','时间：{date}  {time}','天气：{weather}','地点：罗穆卢斯腹地 · 环形山谷'])
node['Results']['Title']='第五关 · 钢之意志'
campaign['Nodes'].append(node)
campaign['Edges'].append(dict(**named('m4-to-m5','拔线 → 钢之意志'),From='mission-four',To='mission-five',Priority=0,Result='success',When=cond()))
p['Maps'].append(m)
assert p['Maps'][:-1]==before['Maps'] and campaign['Nodes'][:-1]==before['Campaigns'][0]['Nodes']
payload=json.dumps(p,ensure_ascii=False,separators=(',',':'))
write_text(path, payload,encoding='utf-8');write_text(ROOT/'1.7/Campaigns/MissionOne.json.gz', payload,encoding='utf-8')
ore=[(x,z) for x in range(325) for z in range(325) if 31.5<=math.hypot(x-162,z-162)<=32.5]
write_text(backup/'manifest.json', json.dumps(dict(previousChaptersUnchanged=True,map=m['Id'],placements=len(m['Objects']),oreCells=len(ore),steel=len(ore)*75,sha256=hashlib.sha256(payload.encode()).hexdigest()),indent=2),encoding='utf-8')
print('Fifth mission appended:',len(m['Objects']),'placements;',len(ore)*75,'steel. Baseline:',backup)
