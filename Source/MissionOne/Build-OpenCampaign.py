"""Add the open-world arrival without regenerating any existing chapter terrain."""
from campaign_io import read_text, write_text
import copy, hashlib, json, shutil
from pathlib import Path
import xml.etree.ElementTree as ET

root=Path(__file__).resolve().parents[2]
path=root/'Campaigns/MissionOne.project.json.gz'
p=json.loads(read_text(path, encoding='utf-8'))
original=copy.deepcopy(p['Maps'][:4])
backup=root/'artifacts/open-campaign/before'
backup.mkdir(parents=True,exist_ok=True)
if not (backup/'MissionOne.project.json').exists(): shutil.copy2(path,backup/'MissionOne.project.json')
campaign=p['Campaigns'][0]
p['Name']=campaign['Name']='Shadow of War'

def named(id,**kwargs): return dict(Id=id,Name=id,Notes='',RelatedObjects=[],**kwargs)
def ref(name,kind='ThingDef',package='redstone.pmpersona'): return dict(Kind=kind,Name=name,Package=package)
def act(kind,id,target='',text='',value=0,steps=None,freeze=False):
    return named(id,Kind=kind,Target=target,Text=text,Value=value,Steps=steps or [],Else=[],When=dict(Op='Always',Key='',Value=0,Children=[]),Freeze=freeze,Arrival='Ground',Origin='',Communication=None,SoundPulse=None)
def place(id,kind,name,x,z,role='',group='',faction='Player',package='redstone.pmpersona'):
    return named(id,Kind=kind,Def=ref(name,'PawnKindDef' if kind in ('Pawn','PawnKind') else 'ThingDef',package),Position=dict(X=x,Z=z),Rotation=0,Count=1,Role=role,Group=group,Faction=faction,Layer='Objects',SpawnAtStart=False,Contents=[])

for id,kind in [('open-mason','PMPH_GestaltZero'),('open-kaldalis','PMPH_Kaldalis')]:
    p['Profiles']=[x for x in p['Profiles'] if x['Id']!=id]
    p['Profiles'].append(named(id,GenerateFrom=ref(kind,'PawnKindDef'),Payload='',References=[],PortabilityErrors=[],Relations=[]))
m5=next(m for m in p['Maps'] if m['Id']=='steel-will')
n5=next(n for n in campaign['Nodes'] if n['Id']=='mission-five')
n5['Roles']={'mason':'open-mason'}
m5['Roles']=['mason']
m5['Objects']=[o for o in m5['Objects'] if o['Id']!='m5-mason']
m5['Objects'].append(place('m5-mason','Pawn','PMPH_GestaltZero',162,180,role='mason'))
def walk(acts):
    for a in acts:
        yield a
        yield from walk(a['Steps'])
arrival=next(a for a in walk(m5['Entry']) if a['Id']=='m5-probes-warp')
arrival['Steps']=[a for a in arrival['Steps'] if a['Id']!='m5-mason-arrival']
arrival['Steps'].append(act('Sequence','m5-mason-arrival',steps=[act('Wait','m5-mason-delay',value=75/60),act('rc.pmcWarpActor','m5-mason-warp','m5-mason',value=3)]))

node=named('open-campaign',Kind='Open',Map='open-landing',Arrival='Position',Inherit=True,Roles={'mason':'open-mason','kaldalis':'open-kaldalis'},Inputs={},Resources=[],Hunger=True,Mood=True,Work=True,Construction=True,Research=True,Storyteller=True,X=1900,Y=232,RefillNeedsOnSuccess=True,
    OpeningTitle=dict(Title='Final Mission',Lines=['Personnel: Akhundelar','Time: {date}  {time}','Weather: {weather}'],ManualStart=True,FadeIn=1,Hold=3,FadeOut=1,TitleColor='#FFD34D',TextColor='#EEEEE6'))
node['Name']='Final Mission'
campaign['Nodes']=[n for n in campaign['Nodes'] if n['Id']!='open-campaign']+[node]
campaign['Edges']=[e for e in campaign['Edges'] if e['Id']!='m5-to-open']+[named('m5-to-open',From='mission-five',To='open-campaign',Result='success',Priority=0,When=dict(Op='Always',Key='',Value=0,Children=[]))]
m=named('open-landing',Width=250,Height=250,Base='Biome',Terrain=ref('Soil','TerrainDef','ludeon.rimworld'),TerrainInitialization=None,Roles=['mason','kaldalis'],Factions=copy.deepcopy(m5['Factions']),FactionRelations=[],Objects=[],Regions=[],Inputs={},Outputs=['success','failure'],Rules=[],Objectives=[],Hud=[],Groups=[],Entry=[],Defeat=[],Components=[],Interactions=[],Rewards=[],PatrolRoutes=[],CombatPatrols=[],NaturalClearings=[])
m['Name']='Romulus landing site'
objects=m['Objects']
for id,kind,name,x,z,role in [('open-pylon','Thing','Pylon',120,130,''),('open-forge','Thing','ProtossForge',130,130,''),('open-mason','Pawn','PMPH_GestaltZero',123,120,'mason'),('open-kaldalis','Pawn','PMPH_Kaldalis',127,120,'kaldalis')]:
    objects.append(place(id,kind,name,x,z,role=role,package='redstonepanda.protossmechcore' if kind=='Thing' else 'redstone.pmpersona'))
for i in range(5): objects.append(place('open-probe-'+str(i),'PawnKind','PM_Probe',117+i*4,115,group='open-probes',package='redstonepanda.protossmechcore'))
objects.append(place('open-center','Marker','',125,125))
for x in range(-16,17):
    for z in [-16,16]:
        if abs(x)>2:objects.append(place(f'open-wall-{x}-{z}','Thing','PMP_RomulusRedWall',125+x,125+z,group='open-outpost',faction='Hostile'))
for z in range(-15,16):
    for x in [-16,16]:
        if abs(z)>2:objects.append(place(f'open-wall-{x}-{z}','Thing','PMP_RomulusRedWall',125+x,125+z,group='open-outpost',faction='Hostile'))
for x,z in [(-10,-10),(10,10),(-10,10),(10,-10),(0,0)]:objects.append(place(f'open-turret-{x}-{z}','Thing','PMP_RomulusSpearTurret',125+x,125+z,group='open-outpost',faction='Hostile'))

translations={'Shadow of War':'战争之影','Final Mission':'最终任务','Personnel: Akhundelar':'行动人员：阿昆德拉','Time: {date}  {time}':'时间：{date}  {time}','Weather: {weather}':'天气：{weather}','Romulus landing site':'罗慕路斯登陆区'}
template=copy.deepcopy(next(a['Communication'] for a in walk(m5['Entry']) if a['Kind']=='Communication'))
def comm(id,speaker,en,zh,portrait='',freeze=False,duration=5):
    translations[en]=zh
    a=act('Communication',id,text=en,freeze=freeze)
    c=copy.deepcopy(template);c.update(SpeakerName=speaker,Duration=duration,Y=260)
    c['Portrait']=ref('PMP_Comms_'+portrait,'CommunicationPortraitDef') if portrait else None
    c['PortraitPath']='';c['Speaker']=''
    if speaker=='C.A.':c['RendererData']={'portrait':'ca'};c['Spectrum']['Color']='#6BDDFF'
    a['Communication']=c;return a
translations.update({'Mason':'梅森','Kaldalis':'卡尔达利斯'})
steps=[act('pmp.open.prepare','open-prepare'),act('Camera','open-wide','open-center','FitMap'),act('rc.openingTitle','open-title'),act('Camera','open-zoom','open-center','42',3),act('Wait','open-survey',value=1.5),act('pmp.open.bombard','open-bombard'),act('Wait','open-impact-settle',value=1.5),act('Camera','open-close','open-center','24',2),act('rc.pmcWarpBuilding','open-pylon-warp','open-pylon',value=3),act('rc.pmcWarpBuilding','open-forge-warp','open-forge',value=3)]
units=[]
for i,(target,kind) in enumerate([(f'open-probe-{j}','rc.pmcWarpUnit') for j in range(5)]+[('open-mason','rc.pmcWarpActor'),('open-kaldalis','rc.pmcWarpActor')]):
    units.append(act('Sequence',target+'-sequence',steps=[act('Wait',target+'-delay',value=i*5/60),act(kind,target+'-warp',target,text='PM_MiningDrill' if i<5 else '',value=3)]))
steps += [act('Parallel','open-units',steps=units),comm('open-mason-line','Mason','Position reached. Beginning the operation.','已抵达位置，开始行动.','Mason',True,3),comm('open-kaldalis-line','Kaldalis','The Legion will fall.','军团必将陨落.','Kaldalis',True,3),act('pmp.open.ready','open-release')]
m['Entry']=[act('Cinematic','open-opening',text='RevealMap',steps=steps)]
for i,(en,zh,duration) in enumerate([
    ('Executor, our vanguard has reached the planet surface.','执行官，我们的先锋队伍已经抵达星球表面.',5),
    ('We need to suppress at least four subnodes to obtain intelligence on the Romulus hub node.','我们需要至少压制四处子节点以获得罗慕路斯枢纽节点的信息.',7),
    ('To end the Legion threat once and for all, we must suppress every subnode. Our allies can protect the reality stabilization anchors we install.','如果我们要一劳永逸地解决军团的威胁，就需要确保所有的子节点都被压制，我们的盟友可以保护安装的现实稳定锚.',11),
    ('The relevant intelligence is displayed on your terminal. Xiya out.','相关情报已经显示在终端上.汐娅通话完毕.',5)]):
    m['Entry'].append(comm('open-ca-'+str(i),'C.A.',en,zh,duration=duration))
m['Entry'].append(act('Set','open-comms-complete','open-intro-complete',value=1))
p['Maps']=[x for x in p['Maps'] if x['Id']!='open-landing']+[m]
assert p['Maps'][:4]==original
payload=json.dumps(p,ensure_ascii=False,separators=(',',':'))
for file in [path,root/'1.7/Campaigns/MissionOne.json.gz']:write_text(file, payload,encoding='utf-8')

for language in ['English','ChineseSimplified (简体中文)']:
    xml=ET.Element('LanguageData')
    for en,zh in translations.items():
        ET.SubElement(xml,'RC_Text_'+hashlib.sha256(en.encode()).hexdigest()[:16]).text=en if language=='English' else zh
    ET.indent(xml,space='  ')
    ET.ElementTree(xml).write(root/'Languages'/language/'Keyed/OpenCampaign.xml',encoding='utf-8',xml_declaration=True)

# The stable def/project IDs remain unchanged so existing references keep resolving.
file=root/'1.7/Defs/Campaigns/MissionOne.xml'
text=read_text(file, encoding='utf-8').replace('Star Eaters — Mission 1: Archive Station','Shadow of War')
write_text(file, text,encoding='utf-8')
cn=root/'Languages/ChineseSimplified (简体中文)/DefInjected'
for rel in ['ScenarioDef/Campaign.xml','RimCampaign.Runtime.CampaignDef/Campaign.xml']:
    file=cn/rel;text=read_text(file, encoding='utf-8').replace('噬星者 第一关 踩点','战争之影');write_text(file, text,encoding='utf-8')
# Keep these XML paths as empty documents so deployment replaces the old definitions.
write_text(root/'1.7/Defs/Scenarios/Scenarios.xml', '<?xml version="1.0" encoding="utf-8"?>\n<Defs />\n',encoding='utf-8')
write_text(cn/'ScenarioDef/Scenarios.xml', '<?xml version="1.0" encoding="utf-8"?>\n<LanguageData />\n',encoding='utf-8')
print('Open arrival added; first four chapter maps unchanged; campaign renamed; old scenario removed.')
