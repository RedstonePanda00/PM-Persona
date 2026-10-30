"""Apply NoF's fifth-chapter script to the existing map without rebuilding terrain."""
from campaign_io import read_text, write_text
from pathlib import Path
import copy,json,sys
root=Path(__file__).resolve().parents[2]
p=json.loads(read_text(root/'Campaigns/MissionOne.project.json.gz', encoding='utf-8-sig'));before=copy.deepcopy(p)
m=next(m for m in p['Maps'] if m['Id']=='steel-will')
def named(id,name=''):return dict(Id=id,Name=name or id,Notes='',RelatedObjects=[])
def cond(op='Always',key='',value=0,children=None):return dict(Op=op,Key=key,Value=value,Children=children or [])
def act(kind,id,target='',text='',value=0,steps=None,freeze=False):return dict(**named(id),Kind=kind,Target=target,Text=text,Value=value,Steps=steps or [],Else=[],When=cond(),Freeze=freeze,Arrival='Ground',Origin='',Communication=None,SoundPulse=None)
def setv(id,key,value):return act('Set',id,key,value=value)
def group(id,actions):return dict(**named(id),Actor='',Actions=actions)
def comm(speaker,text,id,freeze=False,source=False):
 a=act('Communication',id,text=text,freeze=freeze)
 portrait={'卡尔达利斯':'Kaldalis','梅森':'Mason','普莱斯':'Price','幽灵':'Ghost','子墨':'Zimo'}.get(speaker)
 a['Communication']=dict(BackgroundOpacity=.28,CropHeight=1,CropWidth=1,CropX=0,CropY=0,Duration=max(3,min(12,len(text)/7)),Portrait=dict(Kind='CommunicationPortraitDef',Name='PMP_Comms_'+portrait,Package='redstone.pmpersona') if portrait else None,PortraitPackage='',PortraitPath='World/WorldObjects/Expanding/Empire' if speaker=='卡珊德拉' else '',PortraitSize=128,Renderer='pmp.communication',RendererData={'side':'enemy' if source else 'ally'},RendererPackage='redstone.pmpersona',Speaker='$source' if source else '',SpeakerName=speaker,Spectrum=dict(BarCount=67,Color='#FFD447',Enabled=True,Height=56,SimulateSpeech=True,Width=340),Width=620,X=16,Y=260)
 return a
rule_template=copy.deepcopy(m['Rules'][0])
def rule(id,actions,when,seconds=1,once=True):
 r=copy.deepcopy(rule_template);r.update(**named(id),Host='',Actions=actions,Events=[dict(Kind='Periodic',Target='',Seconds=seconds)],When=when,Once=once,Enabled=True,Position=dict(X=162,Z=162),ShowOnMap=True);m['Rules'].append(r)
def active(*cs):return cond('And',children=[cond('Equal','m5-ready',1),cond('Equal','m5-ending',0),*cs])

# Starting supplies and defenders remain authored placements; native actions handle spawning.
for component in m['Components']:
 if component['Type']=='PMP.Campaign.MissionFiveMapComponent':
  for key in ('orePerCell','oreInner','oreOuter'):component['Settings'].pop(key,None)
  component['Settings'].update(oreMinX='150',oreMaxX='173',oreMinZ='124',oreMaxZ='131')
m['Objects']=[o for o in m['Objects'] if o['Group'] not in ('m5-defenders','m5-starting-fuel','m5-drills')]
probe=next(o for o in m['Objects'] if o['Id']=='m5-probe-0')
for i,kind in enumerate(['PM_Sentinel']*3+['PM_Sentry']):
 o=copy.deepcopy(probe);o.update(**named(f'm5-defender-{i}'),Group='m5-defenders',Position=dict(X=148+i*8,Z=178));o['Def']['Name']=kind;m['Objects'].append(o)
for i in range(3):
 o=copy.deepcopy(probe);o.update(**named(f'm5-defender-{i+4}'),Group='m5-defenders',Position=dict(X=154+i*8,Z=174));o['Def'].update(Name='PMP_Prometheus',Package='redstone.pmpersona');m['Objects'].append(o)
for i,count in enumerate([150]*6+[100]):
 o=copy.deepcopy(next(o for o in m['Objects'] if o['Id']=='m5-storage-0'))
 o.update(**named(f'm5-fuel-{i}'),Group='m5-starting-fuel',Position=dict(X=150+i*4,Z=148),Count=count)
 o['Def']=dict(Kind='ThingDef',Name='Chemfuel',Package='ludeon.rimworld');m['Objects'].append(o)
for i,(x,z) in enumerate((x,z) for z in (125,129) for x in range(151,173,3)):
 o=copy.deepcopy(next(o for o in m['Objects'] if o['Id']=='m5-storage-0'))
 o.update(**named(f'm5-drill-{i}'),Group='m5-drills',Position=dict(X=x,Z=z),Rotation=0)
 o['Def']=dict(Kind='ThingDef',Name='DeepDrill',Package='ludeon.rimworld');m['Objects'].append(o)

brief=[
 ('卡尔达利斯','执行官，根据你的指示，我们已经通知了卡珊德拉骑士，她的部队会立刻以稳定状态的子节点为核心建立起防线。',0),
 ('卡尔达利斯','净化者会为您赴汤蹈火，执行官，我们找到了一处绝佳的地点，四面环山，附近有四处罗穆卢斯子节点。',1),
 ('梅森','格式塔小队听从您的调遣指挥官，我们会负责引导军团战士，各小队已在子节点附近就绪，只要你一声令下我们就会发动袭击。',1),
 ('普莱斯','Bravo-0-6就绪，我会负责东北方向，阿德勒和贝尔负责东南。',2),
 ('幽灵','我和子墨会负责西北方向。',3),
 ('子墨','可不是吗老兄，顺便一提长官，卡玛和林薇已经去西南了。',4),
 ('梅森','袭击会非常凶猛，不过我们相信您的判断。我们拖延的时间越长，帝国对据点的控制就越牢固。',5),
 ('梅森','梅森通话完毕。',5)]
lines=[]
for i,(speaker,text,stage) in enumerate(brief):lines.extend([setv(f'm5-brief-stage-{i}','m5-brief-stage',stage),comm(speaker,text,f'm5-brief-line-{i}',True)])
lines.append(setv('m5-brief-done','m5-brief-done',1))
opening=[act('Parallel','m5-briefing',steps=[act('pmp.m5.brief','m5-planar-brief',freeze=True),act('Sequence','m5-brief-lines',steps=lines)]),act('Camera','m5-wide','m5-overview','FitMap'),act('rc.openingTitle','m5-title'),act('Wait','m5-wide-hold',value=2,freeze=True),act('Camera','m5-close','m5-base','24',3,freeze=True)]
def warp_batch(id,kind,targets,text=''):
 return act('Parallel',id,steps=[act('Sequence',f'{id}-{i}',steps=[act('Wait',f'{id}-delay-{i}',value=i*5/60),act(kind,f'{id}-warp-{i}',target,text.get(target,'') if isinstance(text,dict) else text,3)]) for i,target in enumerate(targets)])
probes=[f'm5-probe-{i}' for i in range(8)]
weapons={p:'PM_MiningDrill' for p in probes}|{f'm5-defender-{i}':'PMP_HardlightRifle' for i in range(4,7)}
opening.extend([act('rc.pmcWarpBuilding','m5-pylon-warp','m5-pylon',value=3),warp_batch('m5-buildings-warp','rc.pmcWarpBuilding',['m5-replicator']+[f'm5-storage-{i}' for i in range(5)]),warp_batch('m5-probes-warp','rc.pmcWarpUnit',probes+[f'm5-defender-{i}' for i in range(7)],weapons)])
opening.extend([act('Spawn',f'm5-supply-{i}',f'm5-fuel-{i}') for i in range(7)])
opening.extend([act('pmp.m5.ready','m5-configure'),act('Camera','m5-resource-view','m5-base','55',2,freeze=True)])
m['Entry']=[act('Cinematic','m5-opening',text='RevealMap',steps=opening),setv('m5-release','m5-ready',1),comm('梅森','我们的时间不多执行官，敌人很快就会淹没我们所在的位置，在此之前我们要尽可能地拖延时间。','m5-control-0'),comm('梅森','我们需要建立防线抵挡他们，好在这里的矿物储量丰富，我们可以用复制器转换一些资源。','m5-control-1'),comm('梅森','通话完毕。','m5-control-2')]
m['Entry'][2:]=[act('Parallel','m5-after-control',steps=[act('Sequence','m5-drill-deployment',steps=[act('Wait','m5-drill-delay',value=5/60),warp_batch('m5-drills-warp','rc.pmcWarpBuilding',[f'm5-drill-{i}' for i in range(16)]),setv('m5-drills-arrived','m5-drills-ready',1)]),act('Sequence','m5-construction-comms',steps=m['Entry'][2:])])]
m['Groups']=[group('m5-extraction',[act('Cinematic','m5-ending-scene',steps=[comm('梅森','你们听到命令了，战士们，执行群体召回！','m5-end-line'),act('Camera','m5-ending-view','m5-base','50',2),act('pmp.m5.recall','m5-recall-all'),act('pmp.m5.finale','m5-bombardment')]),act('pmp.m5.finish','m5-finish')])]
for kind,speaker,text in [
 ('PMP_M3Believer','信徒','你们无法抵抗军神的怒火！'),('PMP_LegionGuard','禁军','我们 会碾碎你们脆弱的防线.'),('PMP_LegionChosen','神选者','你们徒劳的抵抗能得到的只有灭亡。'),('PMP_LegionCenturion','百夫长','为了军神的荣耀，冲锋！')]:m['Groups'].append(group('m5-contact-'+kind,[comm(speaker,text,'m5-contact-line-'+kind,source=True)]))
for i,(speaker,text) in enumerate([('梅森','这可不妙...罗穆卢斯派出了他的冠军勇士。'),('卡尔达利斯','敌人派出了一名冠军勇士。'),('普莱斯','执行官，这里是Bravo-0-6，一名冠军勇士朝我们的基地来了。'),('幽灵','指挥官，一名冠军勇士正在接近我方基地。'),('子墨','长官，冠军勇士来了。')]):m['Groups'].append(group(f'm5-champion-warning-{i}',[comm(speaker,text,f'm5-champion-line-{i}')]))
for i,(speaker,text) in enumerate([('卡尔达利斯','敌人扑过来了，战士们，亮出光刃！'),('幽灵','指挥官，军团已经抵达西北方向，准备冲击我们的基地。'),('卡尔达利斯','执行官，东南方向检测到军团的战士。'),('普莱斯','军团的人上钩了长官，他们在基地的东北方向集结。')]):m['Groups'].append(group(f'm5-direction-{i}',[comm(speaker,text,f'm5-direction-line-{i}')]))

slot=copy.deepcopy(next(o for o in m['Objects'] if o['Kind']=='PawnKind' and o['Faction']=='Hostile'))
m['Objects']=[o for o in m['Objects'] if not o['Group'].startswith('m5-wave-') and not o['Group'].startswith('m5-assault-') and not o['Group'].startswith('m5-endless-')]
m['Rules']=[]
corners=[('sw','西南',18,18,1,1),('nw','西北',18,306,1,-1),('se','东南',306,18,-1,1),('ne','东北',306,306,-1,-1)]
def slots(group,code,count,tier):
 _,label,x,z,sx,sz=next(c for c in corners if c[0]==code)
 kinds=['PMP_Warhound' if i%4==2 else 'PMP_M3Believer' for i in range(count)]
 if tier>=1:kinds[-1]='PMP_LegionCenturion'
 if tier>=2:kinds[-2]='PMP_LegionGuard'
 if tier>=3:kinds[-3]='PMP_LegionChosen'
 if tier>=4:kinds[-4]='PMP_SanctuaryChampion'
 for i,kind in enumerate(kinds):
  o=copy.deepcopy(slot);o.update(**named(f'{group}-{i}'),Group=group,Position=dict(X=x+sx*(i%5)*2,Z=z+sz*(i//5)*2));o['Def']['Name']=kind;m['Objects'].append(o)
 return label
times=[180,240,310,370,400,440,490,520,560,600,640,670,700,740,770,800,830,860]
for i,second in enumerate(times):
 code=corners[i%4][0];count=min(14,4+2*(i//3));tier=4 if i==16 else 3 if i>=12 else 2 if i>=8 else 1 if i>=4 else 0
 name=f'm5-assault-{i:02}';label=slots(name,code,count,tier)
 rule(f'm5-wave-{i:02}',[act('pmp.m5.wave',f'm5-arrival-{i:02}',name,label,count),setv(f'm5-next-{i}','m5-wave-index',i+1)],active(cond('AtLeast','m5-seconds',second),cond('Equal','m5-wave-index',i)))
for code,label,*_ in corners:slots('m5-endless-'+code,code,18,4 if code=='sw' else 3)
rule('m5-endless',[setv('m5-endless-wait','m5-endless-pending',1),act('pmp.m5.wave','m5-endless-arrival','cycle',value=18),setv('m5-endless-release','m5-endless-pending',0)],active(cond('AtLeast','m5-seconds',900),cond('AtLeast','m5-wave-index',18),cond('Equal','m5-endless-pending',0)),seconds=90,once=False)
rule('m5-first-muster',[comm('卡尔达利斯','执行官，军团正在西南方向集结。','m5-muster-line')],active(cond('AtLeast','m5-seconds',150)))
for id,t,text in [('half',450,'感谢你们的支援，执行官，我们已经搭建起了初步的防线，工程进度顺利。'),('three-quarter',675,'重型炮台和结构墙已经加固完毕，就快好了，执行官。'),('almost',855,'物流系统搭建完毕，就快好了，执行官，只要一小会儿。'),('ready',900,'据点已就绪，执行官，你们随时可以撤离，感谢你们的支援。')]:
 actions=[comm('卡珊德拉',text,'m5-progress-'+id)]
 if id=='ready':actions.insert(0,act('Objective','m5-hold-done','m5-hold','Complete'))
 rule('m5-progress-'+id,actions,active(cond('AtLeast','m5-seconds',t)))
m['Notes']='NoF 草稿：固定俯视沙盘、四向诱敌、十五分钟坚守、单位与建筑召回、行星碎裂炮收尾。'
assert p['Maps'][:-1]==before['Maps'][:-1] and p['Campaigns']==before['Campaigns']
payload=json.dumps(p,ensure_ascii=False,separators=(',',':'))
for file in ['Campaigns/MissionOne.project.json.gz','1.7/Campaigns/MissionOne.json.gz']:write_text(root/file, payload,encoding='utf-8')
print('Revised fifth chapter; first four maps and campaign graph preserved.')
