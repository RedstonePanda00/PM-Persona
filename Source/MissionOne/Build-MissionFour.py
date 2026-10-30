"""Append the first playable mission-four map. Refuses to overwrite an authored map."""
from campaign_io import read_text, write_text
from pathlib import Path
import copy, json, hashlib, shutil, datetime, xml.etree.ElementTree as ET
ROOT=Path(__file__).resolve().parents[2]
path=ROOT/'Campaigns/MissionOne.project.json.gz'
p=json.loads(read_text(path, encoding='utf-8-sig'))
assert not any(m['Id']=='romulus-subnode' for m in p['Maps']), 'Fourth map already exists; edit it in place instead of regenerating.'
backup=ROOT/'artifacts'/('mission-four-baseline-'+datetime.datetime.now().strftime('%Y%m%d-%H%M%S'))
backup.mkdir(parents=True)
for name in ['Campaigns/MissionOne.project.json.gz','1.7/Campaigns/MissionOne.json.gz']:
 shutil.copy2(ROOT/name,backup/Path(name).name)
before=copy.deepcopy(p)
m=copy.deepcopy(p['Maps'][2]);m.update(Id='romulus-subnode',Name='第四关 · 拔线',Notes='战斗推进；短交互启动 60 秒自动安装，完成后原地群体召回。所有驻守、波次和支线均可在编辑器调整。',Width=300,Height=240,Roles=['hero'])
for k in ['Objects','Rules','Entry','Defeat','Groups','Rewards','Objectives','Hud','Regions','CombatPatrols','Interactions','Components','NaturalClearings','PatrolRoutes']:m[k]=[]
m['Factions']=copy.deepcopy(p['Maps'][2]['Factions'][:2]);m['Terrain']={'Kind':'TerrainDef','Name':'Gravel','Package':'ludeon.rimworld'}
m['Inputs']={};m['TerrainInitialization']=None
def named(id,name=''):return dict(Id=id,Name=name,Notes='',RelatedObjects=[])
def cond(op='Always',key='',value=0,children=None):return dict(Op=op,Key=key,Value=value,Children=children or [])
def act(kind,id,target='',text='',value=0,steps=None,freeze=False,when=None,**kw):
 a=dict(**named(id),Kind=kind,Target=target,Text=text,Value=value,Steps=steps or [],Else=[],When=when or cond(),Freeze=freeze,Arrival='Ground',Origin='',Communication=None,SoundPulse=None);a.update(kw);return a
def ref(name,kind='ThingDef',package=None):return dict(Kind=kind,Name=name,Package=package or ('redstone.pmpersona' if name.startswith('PMP') else 'ludeon.rimworld'))
template=next(o for o in p['Maps'][2]['Objects'] if o['Kind']=='Marker')
def obj(id,name,x,z,kind='Thing',faction='Hostile',group='',spawn=True,stuff=None,rotation=0,**kw):
 o=copy.deepcopy(template);o.update(Id=id,Name=id,Kind=kind,Def=ref(name,'PawnKindDef' if kind=='PawnKind' else 'TerrainDef' if kind=='Terrain' else 'ThingDef'),Position={'X':x,'Z':z},Faction=faction,Group=group,SpawnAtStart=spawn,Stuff=ref(stuff) if stuff else None,Layer='Terrain' if kind=='Terrain' else 'Auxiliary' if kind=='Marker' else 'Objects',Rotation=rotation);o.update(kw);m['Objects'].append(o);return o
def marker(id,x,z):return obj(id,'',x,z,'Marker','Player')
terrain={};walls=set()
def floor(x0,z0,x1,z1,name='Concrete'):
 for x in range(x0,x1+1):
  for z in range(z0,z1+1):terrain[x,z]=name
def wallbox(x0,z0,x1,z1,gaps=()):
 cells={(x,z) for x in range(x0,x1+1) for z in [z0,z1]}|{(x,z) for z in range(z0,z1+1) for x in [x0,x1]}
 for cell in cells:
  if not any(a<=cell[0]<=c and b<=cell[1]<=d for a,b,c,d in gaps):walls.add(cell)
def sand(x,z,horizontal=True,n=7):
 for i in range(n):obj(f'm4-cover-{x}-{z}-{i}','Sandbags',x+i if horizontal else x,z if horizontal else z+i,stuff='Cloth')
for rect in [(120,15,180,44),(32,55,83,153),(215,55,265,148),(87,80,215,215),(130,50,174,76),(145,101,177,132),(222,166,254,198)]:floor(*rect)
floor(135,157,190,208,'MetalTile');floor(96,112,133,157,'PavedTile')
# Broad connecting lanes, including space for the Immortal.
for rect in [(60,47,235,58),(76,122,146,136),(130,136,205,150),(205,112,240,128),(185,176,238,186),(108,208,122,238),(202,208,216,238)]:floor(*rect)
wallbox(87,80,215,215,[(87,122,87,136),(215,112,215,128),(215,176,215,186),(108,215,122,215),(202,215,214,215)])
wallbox(32,60,83,153,[(49,60,70,60),(83,122,83,136)])
wallbox(96,112,133,157,[(96,125,96,135),(133,128,133,140)])
wallbox(135,157,190,208,[(150,157,177,157),(190,176,190,186),(135,177,135,190),(153,208,175,208)])
for x,z in sorted(walls):obj(f'm4-wall-{x}-{z}','PMP_RomulusRedWall',x,z,stuff='Steel')
for (x,z),name in sorted(terrain.items()):obj(f'm4-floor-{x}-{z}',name,x,z,'Terrain','Neutral')
marker('m4-overview',150,129);marker('m4-focus',150,30);marker('m4-defense-point',166,170)
obj('m4-mason','',150,30,'Pawn','Player',spawn=False,Role='hero',Rotation=0)
obj('m4-subnode','PMP_RomulusSubnode',166,181)
for i,(x,z) in enumerate([(105,118),(113,118),(121,118),(105,150),(113,150),(121,150)]):
 obj('m4-library-'+str(i),'Bookcase',x,z,stuff='Steel',Contents=[dict(Def=ref('Schematic'),Stuff=None,Count=10,Book=None)])
def unit(id,kind,x,z,group,spawn=True):return obj(id,kind,x,z,'PawnKind','Hostile',group,spawn)
def defend(group,x,z,radius=20):
 marker(group+'-post',x,z);m['Entry'].append(act('rc.defendGroup',group+'-defend',group,group+'-post',radius))
def turret(id,x,z,group=''):
 obj(id,'PMP_RomulusSpearTurret',x,z,group=group)
 # Existing spear turrets are self-powered; their definition has no power trader.
for group,points in [('m4-flank',[(53,99),(66,118),(74,139)]),('m4-front',[(224,92),(249,96),(232,115),(247,130)]),('m4-court',[(144,169),(182,173),(145,196),(183,198)])]:
 for i,(x,z) in enumerate(points):unit(group+str(i),'PMP_M3Believer',x,z,group)
 defend(group,*points[-1],22)
unit('m4-centurion-front','PMP_LegionCenturion',239,137,'m4-front')
unit('m4-centurion-court','PMP_LegionCenturion',175,197,'m4-court')
unit('m4-guard-court','PMP_LegionGuard',157,198,'m4-court')
for i,(x,z) in enumerate([(53,111),(67,128),(226,125),(249,118)]):unit('m4-main-hound-'+str(i),'PMP_Warhound',x,z,'m4-flank' if x<100 else 'm4-front')
for i,(x,z) in enumerate([(226,105),(251,124),(145,185),(183,190)]):turret('m4-main-turret-'+str(i),x,z)
for x,z,h in [(48,105,True),(69,133,True),(220,100,True),(247,116,True),(142,176,True),(179,183,False),(154,200,True)]:sand(x,z,h)

def rule(id,event,actions,when=None,seconds=0,target='',host='',position=(150,120),radius=5,once=True):
 r=copy.deepcopy(p['Maps'][2]['Rules'][-1]);r.update(Id=id,Name=id,Notes='',Actions=actions,Events=[dict(Kind=event,Target=target,Seconds=seconds)],When=when or cond(),Host=host,Position=dict(X=position[0],Z=position[1]),Radius=radius,Once=once,Enabled=True,ShowOnMap=True);m['Rules'].append(r);return r
def comm(who,text,id,freeze=False):
 portraits={'梅森':'Mason','普莱斯':'Price','卡尔达利斯':'Kaldalis'}
 a=act('Communication',id,text=text,freeze=freeze)
 # Every Kaldalis line binds to a portrait, never to the life of a pawn shell.
 a['Communication']=dict(BackgroundOpacity=.28,CropHeight=1,CropWidth=1,CropX=0,CropY=0,Duration=max(3,min(7,len(text)/6)),Portrait=ref('PMP_Comms_'+portraits[who],'CommunicationPortraitDef') if who in portraits else None,PortraitPackage='',PortraitPath='',PortraitSize=128,Renderer='pmp.communication',RendererData={'side':'ally'},RendererPackage='redstone.pmpersona',Speaker='',SpeakerName=who,Spectrum=dict(BarCount=67,Color='#FFD447',Enabled=True,Height=56,SimulateSpeech=True,Width=340),Width=620,X=16,Y=260)
 return a
ready=cond('Equal','m4-ready',1)
for i,(x,z,rewards) in enumerate([(152,63,['PM_Sentinel','PM_Sentinel','PM_Sentinel','PM_Sentry']),(161,116,['PM_PurifierImmortal']),(238,183,['PMPH_Kaldalis'])],1):
 group=f'm4-outpost-{i}';core=f'{group}-obelisk'
 obj(core,'PMP_RomulusObelisk',x,z+6,group=group)
 for j,(dx,dz) in enumerate([(-9,-5),(9,-5),(-8,9),(8,9)][:3 if i==1 else 4]):unit(f'{group}-guard-{j}','PMP_M3Believer',x+dx,z+dz,group)
 unit(group+'-hound','PMP_Warhound',x-5,z-8,group)
 turret(group+'-turret',x+9,z+5,group)
 sand(x-10,z-9,n=7);sand(x+4,z-9,n=7);sand(x-11,z+3,False,7)
 defend(group,x,z,17)
 pylon=obj(group+'-pylon','Pylon',x,z-2,faction='Player',spawn=False);pylon['Def']['Package']='redstonepanda.protossmechcore'
 arrivals=[]
 for j,kind in enumerate(rewards):
  id=f'{group}-ally-{j}';o=obj(id,kind,x-6+j*4,z-2,'PawnKind','Player','m4-allies',False)
  if kind.startswith('PM_'):o['Def']['Package']='redstonepanda.protossmechcore'
  arrivals.append(act('rc.pmcWarpUnit',id+'-warp',id,'PMP_HardlightRifle' if kind=='PM_Sentry' else '',3))
 lines=[comm('警戒者','战士，已经苏醒.',f'm4-s{i}-line1'),comm('梅森','欢迎加入，战士们。',f'm4-s{i}-line2')] if i==1 else [comm('不朽者','我重回战场.',f'm4-s{i}-line1'),comm('梅森','En taro Artanis，恺撒，我的荣幸。',f'm4-s{i}-line2')] if i==2 else [comm('卡尔达利斯','我来助你一臂之力，梅森.',f'm4-s{i}-line1'),comm('梅森','很高兴见到你，卡尔达利斯.',f'm4-s{i}-line2'),comm('卡尔达利斯','敌人将在我的战刃之下陨落.',f'm4-s{i}-line3')]
 rule(group+'-clear','Periodic',[act('Objective',group+'-complete',group,text='Complete'),act('rc.pmcWarpBuilding',group+'-building-warp',pylon['Id'],value=3),act('Parallel',group+'-units-warp',steps=arrivals)]+lines,cond('And',children=[ready,cond('rc.targetsCleared',group),cond('Less','m4-stage',2)]),seconds=.5,position=(x,z))
 hint=['梅森，我们的战士渴望敌人的鲜血，端掉那座哨站，我们就能投送一些增援来帮你。','另一处哨站，梅森，端掉他，我们会派一些战士来帮你。','那是最后一处哨站了，梅森。'][i-1]
 r=rule(group+'-hint','RangeEnter',[comm('卡尔达利斯',hint,group+'-hint-line')],ready,position=(x,z),radius=28);r['Detect']='Specific';r['Subject']='role:hero'
 m['Objectives'].append(dict(**named(group,f'摧毁第 {i} 哨所方尖碑并清除守军（可选）'),Mode='Standard',Role='',Required=False))

m['Objectives'][:0]=[dict(**named('m4-anchor','安装现实稳定锚，压制枢纽'),Mode='Standard',Role='',Required=True)]
# Conventional approach prompts remain editable spatial triggers.
for id,pos,radius,lines in [('front',(237,78),18,['正面强攻并不理智。']),('flank',(59,70),17,['看起来是一处维护通道。','正适合潜入。'])]:
 r=rule('m4-'+id+'-hint','RangeEnter',[comm('梅森',line,'m4-'+id+'-line-'+str(j)) for j,line in enumerate(lines)],ready,position=pos,radius=radius);r['Detect']='Specific';r['Subject']='role:hero'

# One automatic timer, not a pawn-bound work task. Waves are authored placements.
for wave,sec,count in [(1,15,4),(2,35,6),(3,50,6)]:
 group='m4-wave-'+str(wave)
 for j in range(count):unit(group+'-'+str(j),'PMP_Warhound' if j%3==0 else 'PMP_M3Believer',(113 if j%2==0 else 208)+(j//2)*2,235,group,False)
 m['Groups'].append(dict(**named(group+'-attack','增援向枢纽进攻'),Actor='$source',Actions=[act('rc.task.move',group+'-move','m4-defense-point',value=8),act('rc.task.engage',group+'-engage','m4-hero-target')]))
 actions=[act('rc.reinforce',group+'-spawn',group,group+'-attack',count),act('pmp.m4.setup',group+'-setup')]
 if wave==1:actions.append(comm('梅森','发现增援。它们正往核心靠近。','m4-wave-line'))
 rule(group+'-trigger','Periodic',actions,cond('And',children=[cond('Equal','m4-stage',1),cond('AtMost','timer:m4-install',60-sec)]),seconds=.5,position=(113,230))
next(o for o in m['Objects'] if o['Id']=='m4-mason')['Group']='m4-hero-target'
interaction=copy.deepcopy(p['Maps'][1]['Interactions'][0]);interaction.update(Id='m4-install',Name='启动自动安装；随后可自由战斗，约 60 秒后完成。',Host='m4-subnode',Label='安装现实稳定锚',UseTicks=120,OneShot=True,When=cond('And',children=[ready,cond('Equal','m4-stage',0)]),BlockCells=[],ObstacleDef='',DestroyHost=False,EscapeCell=None,Actions=[act('Set','m4-install-start','m4-stage',value=1),act('Timer','m4-install-timer','m4-install',value=60),comm('梅森','开始安装现实稳定锚。','m4-install-line'),comm('卡尔达利斯','敌人扑过来了，圣堂武士，准备战斗。','m4-defend-line')]);m['Interactions']=[interaction]
rule('m4-almost-done','Periodic',[comm('普莱斯','只剩最后一段了，梅森。','m4-almost-line')],cond('And',children=[cond('Equal','m4-stage',1),cond('AtMost','timer:m4-install',10)]),seconds=.5)
ending=[act('Set','m4-suppressed-state','m4-stage',value=2),act('Objective','m4-anchor-done','m4-anchor','Complete'),act('Cinematic','m4-ending',target='hero',steps=[act('pmp.m2.focus','m4-focus-end','hero','m4-focus',freeze=True),act('Camera','m4-ending-camera','m4-focus','18',1.2,freeze=True),comm('梅森','安装完成，快，折跃上船！','m4-end-line1',True),comm('普莱斯','我们已经锁定你的位置，正在执行群体召回。','m4-end-line2',True),act('rc.pmcRecall','m4-recall-all'),act('Camera','m4-node-collapse-camera','m4-subnode','24',.8,freeze=True),act('pmp.suppressSubnode','m4-suppress','m4-subnode'),act('Wait','m4-node-collapse-wait',value=1.5)]),act('rc.finish','m4-finish',text='稳定锚已安装，节点已压制。梅森及本关仍在场的增援已折跃返回曙光号。')]
rule('m4-install-complete','Periodic',ending,cond('And',children=[cond('Equal','m4-stage',1),cond('AtMost','timer:m4-install',0)]),seconds=.25)
for event in ['Death','Downed']:rule('m4-mason-'+event.lower(),event,[act('Fail','m4-fail-'+event,text='梅森已失去战斗能力。')],host='m4-mason')

opening=act('Cinematic','m4-opening','hero','RevealMap',steps=[act('Camera','m4-camera-overview','m4-overview','FitMap'),act('rc.openingTitle','m4-title'),act('Wait','m4-overview-hold',value=2,freeze=True),act('Camera','m4-camera-mason','m4-focus','18',3,freeze=True),act('rc.pmcWarpActor','m4-mason-warp','m4-mason',value=3),act('pmp.m4.setup','m4-setup-arrival'),comm('梅森','曙光号这里是梅森，我已抵达目标位置。','m4-opening-line1',True),comm('普莱斯','收到，保持警惕，梅森。','m4-opening-line2',True)])
m['Entry']=[act('pmp.m4.setup','m4-setup-initial')]+m['Entry']+[opening,act('Set','m4-release','m4-ready',value=1)]
for typ,name in [('PMP.Campaign.MissionEffectsMapComponent','罗穆卢斯阵营与角色装备'),('RimCampaign.Runtime.SceneInteractionMapComponent','安装稳定锚交互')]:m['Components'].append(dict(**named('m4-component-'+str(len(m['Components'])),name),Enabled=True,Settings={},Type=typ))
m['Hud']=[dict(**named('m4-goals'),Kind='Objectives',Binding='',Text='',X=16,Y=100,Width=380,Height=28,Maximum=100),dict(**named('m4-timer'),Kind='Number',Binding='timer:m4-install',Text='自动安装剩余秒数',X=16,Y=220,Width=300,Height=28,Maximum=60)]
campaign=p['Campaigns'][0];node=copy.deepcopy(campaign['Nodes'][2]);node.update(Id='mission-four',Name='第四关 · 拔线',Map=m['Id'],X=1220,Roles={'hero':'mason'})
node['OpeningTitle'].update(Title='拔线',Hold=3,FadeOut=1,Lines=['行动人员：大卫.梅森，格式塔零号','时间：{date}  {time}','天气：{weather}','地点：罗穆卢斯枢纽子节点']);node['Results']['Title']='第四关 · 拔线'
campaign['Nodes'].append(node);campaign['Edges'].append(dict(**named('m3-to-m4','反抗 → 拔线'),From='mission-three',To='mission-four',Priority=0,Result='success',When=cond()));p['Maps'].append(m)
assert p['Maps'][:3]==before['Maps']
assert campaign['Nodes'][:3]==before['Campaigns'][0]['Nodes']
payload=json.dumps(p,ensure_ascii=False,separators=(',',':'))
write_text(path, payload,encoding='utf8');write_text(ROOT/'1.7/Campaigns/MissionOne.json.gz', payload,encoding='utf8')
write_text(backup/'manifest.json', json.dumps({'originalMapsUnchanged':True,'map':m['Id'],'objects':len(m['Objects']),'triggers':len(m['Rules']),'sha256':hashlib.sha256(payload.encode()).hexdigest()},indent=2),encoding='utf8')
print('Added fourth mission:',len(m['Objects']),'placements;',len(m['Rules']),'rules. Backup:',backup)
