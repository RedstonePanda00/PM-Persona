"""Change only the final objective/scene and remove the redundant third construction wave."""
from campaign_io import read_text, write_text
from pathlib import Path
import json, shutil, datetime, hashlib, xml.etree.ElementTree as ET
ROOT=Path(__file__).resolve().parents[2]
BACKUP=ROOT/'artifacts'/('mission-three-ending-backup-'+datetime.datetime.now().strftime('%Y%m%d-%H%M%S'))
BACKUP.mkdir()
translations={
 '消灭冠军勇士':'Eliminate the Champion Warrior',
 '摧毁最后的罗穆卢斯枢纽':'Destroy the final Romulus hub',
 '关闭第 3 处护盾并清理阵地（可选）':'Disable the third shield and clear its position (optional)',
 '冠军勇士已被消灭，最后的罗穆卢斯枢纽已被摧毁。附近出现新的异常信号，贝尔与阿德勒已前往侦察。':'The Champion Warrior is dead and the final Romulus hub has been destroyed. A new anomalous signal has appeared nearby; Bell and Adler are investigating.'
}
def action(kind,ident,**kwargs):
 return dict(Id=ident,Name='',Kind=kind,Target='',Text='',Value=0,Freeze=False,Steps=[],Else=[],When=dict(Op='Always',Key='',Value=0,Children=[]),**{k:v for k,v in kwargs.items() if k not in ['Target','Text','Value','Freeze','Steps']}) | kwargs
for relative in ['Campaigns/MissionOne.project.json.gz','1.7/Campaigns/MissionOne.json.gz']:
 path=ROOT/relative;shutil.copy2(path,BACKUP/path.name)
 p=json.loads(read_text(path, encoding='utf-8-sig'));m=next(m for m in p['Maps'] if m['Id']=='romulus-sanctuary')
 oldobjects={o['Id']:o for o in m['Objects']}
 m['Rules']=[r for r in m['Rules'] if r['Id']!='m3-engineers-3']
 m['Objects']=[o for o in m['Objects'] if o['Group'] not in ['engineers-3','outpost-plans-3']]
 for obj in m['Objectives']:
  if obj['Id'].startswith('capture-'):obj['Required']=False
  if obj['Id']=='capture-3':obj['Name']='关闭第 3 处护盾并清理阵地（可选）'
  if obj['Id']=='hub':obj['Name']='摧毁最后的罗穆卢斯枢纽'
 if not any(o['Id']=='champion' for o in m['Objectives']):m['Objectives'].append(dict(Id='champion',Name='消灭冠军勇士',Notes='',RelatedObjects=['m3-champion'],Mode='Standard',Required=True,Role=''))
 for ident in ['m3-ending-focus','m3-ending-exit','m3-cassandra']:
  if any(o['Id']==ident for o in m['Objects']):continue
  o=dict(Id=ident,Name=ident,Notes='',RelatedObjects=[],BindRole='',Book=None,Boss='',Contents=[],Count=1,Def=dict(Kind='ThingDef',Name='',Package='ludeon.rimworld'),Faction='Neutral',GraphicIndex=None,Group='',Kind='Marker',Layer='Objects',Position=dict(X=474,Z=90),Role='',Rotation=0,SpawnAtStart=False,Stuff=None)
  if ident=='m3-cassandra':o.update(Kind='PawnKind',Name='卡珊德拉（结尾演出）',Faction='Empire',Def=dict(Kind='PawnKindDef',Name='PMP_ImperialKnight',Package='redstone.pmpersona'))
  m['Objects'].append(o)
 victory=next(r for r in m['Rules'] if any(e['Kind']=='AssaultVictory' for e in r['Events']))
 if not any(a['Id']=='m3-ending-cinematic' for a in victory['Actions']):
  original=victory['Actions'];lines=[a for a in original if a['Kind']=='Communication']
  final=next(a for a in original if a['Kind']=='rc.finish');final['Text']=list(translations)[-1]
  neutral=next(a for a in original if a['Kind']=='pmp.m3.neutral')
  steps=[action('pmp.m3.endingStage','m3-ending-stage'),action('Camera','m3-ending-camera',Target='m3-ending-focus',Text='12',Value=1.5)]
  for line in lines:
   line['Freeze']=True;steps.append(line)
   if line['Text']=='帮我谢谢你们的指挥官。':
    steps.extend([action('pmp.m3.endingExit','m3-ending-choose-exit'),action('Move','m3-ending-walk-away',Target='m3-cassandra',Text='m3-ending-exit'),action('Despawn','m3-ending-despawn',Target='m3-cassandra'),neutral])
  victory['Actions']=[action('Objective','m3-complete-hub',Target='hub',Text='Complete'),action('Objective','m3-complete-champion',Target='champion',Text='Complete'),action('Cinematic','m3-ending-cinematic',Target='hero',Steps=steps),final]
 for scene in victory['Actions']:
  if scene['Id']=='m3-ending-cinematic':
   steps=scene['Steps']
   exit_ids={'m3-ending-choose-exit','m3-ending-walk-away','m3-ending-despawn','m3-ending-pause'}
   departure=[s for s in steps if s['Id'] in exit_ids and s['Id']!='m3-ending-pause']
   neutral=next(s for s in steps if s['Kind']=='pmp.m3.neutral')
   steps=[s for s in steps if s['Id'] not in exit_ids and s['Kind']!='pmp.m3.neutral']
   reply=next(i for i,s in enumerate(steps) if s.get('Text')=='我会的。')
   steps[reply+1:reply+1]=[action('Wait','m3-ending-pause',Value=.5)]+departure+[neutral]
   scene['Steps']=steps
   for step in steps:
    if step['Id']=='m3-ending-walk-away':step['Kind']='pmp.m3.endingWalk'
   for step in scene['Steps']:
    if step['Kind']=='Communication':step['Communication']['RendererData']['anchor']='bottom'
 # Every surviving existing placement is byte-for-byte equivalent as a JSON value.
 assert all(o==oldobjects[o['Id']] for o in m['Objects'] if o['Id'] in oldobjects)
 write_text(path, json.dumps(p,ensure_ascii=False,separators=(',',':')),encoding='utf-8')
 print(relative,'updated; retained placements unchanged')
for folder,values in [('English',translations),('ChineseSimplified (简体中文)',{s:s for s in translations})]:
 path=ROOT/'Languages'/folder/'Keyed'/'CampaignText.xml';tree=ET.parse(path);root=tree.getroot()
 for chinese,text in values.items():
  key='RC_Text_'+hashlib.sha256(chinese.encode()).hexdigest()[:16]
  node=root.find(key)
  if node is None:node=ET.SubElement(root,key)
  node.text=text
 ET.indent(tree,space='  ');tree.write(path,encoding='utf-8',xml_declaration=True)
