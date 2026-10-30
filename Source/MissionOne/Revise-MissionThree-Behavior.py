from campaign_io import read_text, write_text
import json,copy
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
base=ROOT/'artifacts/mission-three-behavior-revision/baseline/MissionOne.project.json'
p=json.loads(read_text(base, encoding='utf-8-sig'))
assert json.loads(read_text(ROOT/'Campaigns/MissionOne.project.json.gz', encoding='utf-8-sig'))==p,'Project changed since backup'
m=next(m for m in p['Maps'] if m['Id']=='romulus-sanctuary');objects={o['Id']:o for o in m['Objects']}
template=next(o for o in m['Objects'] if o['Def']['Name']=='Turret_AutoChargeBlaster')
for i,(x,z) in enumerate([(330,94),(388,153),(411,159)],1):
 o=copy.deepcopy(template);o.update(Id=f'm3-route-charge-{i}',Name=f'梅森路线轻防御炮台 {i}',Position={'X':x,'Z':z},Group='');m['Objects'].append(o)
m['Regions'].append({'Id':'m3-champion-northern-arena','Name':'冠军北部交战边界','Notes':'不允许进入南部战场或绕出营地','RelatedObjects':[],'Min':{'X':220,'Z':175},'Max':{'X':482,'Z':309}})
m['Components'].append({'Id':'m3-champion-arena','Name':'冠军待命、激活与战区限制','Notes':'通用框架组件；rules 指定角色、锚点、区域、激活变量与目标阵营','RelatedObjects':[],'Enabled':True,'Type':'RimCampaign.Runtime.BossArenaMapComponent','Settings':{'rules':json.dumps([{'Actor':'m3-champion','Anchor':'champion-post','Region':'m3-champion-northern-arena','ActivationVariable':'sector-2-captured','TargetFaction':'Empire'}],separators=(',',':'))}})
m['Entry']=[a for a in m['Entry'] if not(a['Kind']=='rc.defendGroup' and a['Target']=='champion-defense')]
champion=next(g for g in m['Groups'] if g['Id']=='m3-champion-tasks')
act=copy.deepcopy(next(a for a in next(g for g in m['Groups'] if g['Id']=='m3-mason-tasks')['Actions'] if a['Kind']=='Set'))
act.update(Id='m3-champion-activation-state',Name='冠军正式加入北部战斗',Target='m3-champion-awake',Value=1)
champion['Actions']=[act]
rule=next(r for r in m['Rules'] if r['Id']=='m3-champion-activate');rule['When']={'Op':'Equal','Key':'sector-2-captured','Value':1,'Children':[]}
objects['extraction']['Position']={'X':474,'Z':90};objects['extraction']['Name']='梅森东南安全待命点'
marker=objects['mason-route-3-4']
for i,(x,z) in enumerate([(443,136),(443,96)],1):
 o=copy.deepcopy(marker);o.update(Id=f'mason-safe-route-{i}',Name=f'东南撤离路点 {i}',Position={'X':x,'Z':z});m['Objects'].append(o)
group=next(g for g in m['Groups'] if g['Id']=='m3-mason-tasks');last='mason-route-1-0';actions=[]
for a in group['Actions']:
 if a['Kind']=='rc.task.move' and a['Target']=='extraction':continue
 if a['Kind']=='WaitUntil' and a['When']['Key']=='sector-3-captured':continue
 if a['Kind']=='rc.task.move':last=a['Target'];a['Text']='Combat'
 elif a['Kind']=='rc.task.operate':a['Text']='Combat'
 elif a['Kind']=='WaitUntil':a.update(Kind='rc.task.hold',Target=last,Value=1,Text='Combat')
 actions.append(a)
move=copy.deepcopy(next(a for a in actions if a['Kind']=='rc.task.move'))
for i,target in enumerate(['mason-safe-route-1','mason-safe-route-2','extraction']):
 a=copy.deepcopy(move);a.update(Id=f'mason-to-safe-{i}',Name='交战后继续前往安全区',Target=target,Value=1);actions.append(a)
a=copy.deepcopy(act);a.update(Id='mason-arrived-safe',Name='梅森已抵达安全点',Target='m3-mason-safe');actions.append(a)
a=copy.deepcopy(move);a.update(Id='mason-hold-safe',Name='安全点待命并自卫',Kind='rc.task.hold',Target='extraction',Value=1,When={'Op':'Equal','Key':'m3-ended','Value':1,'Children':[]});actions.append(a)
group['Actions']=actions
for file in ['Campaigns/MissionOne.project.json.gz','1.7/Campaigns/MissionOne.json.gz']:write_text(ROOT/file, json.dumps(p,ensure_ascii=False,separators=(',',':')),encoding='utf-8')
print('Added three route turrets; bounded champion arena; reactive movement/hold; southeast extraction.')
