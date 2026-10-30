"""Render an author-review diagram from the current project, never an input to map generation."""
from campaign_io import read_text
import json
from pathlib import Path
from PIL import Image,ImageDraw,ImageFont
R=Path(__file__).resolve().parents[2]
m=next(m for m in json.loads(read_text(R/'Campaigns/MissionOne.project.json.gz', encoding='utf-8-sig'))['Maps'] if m['Id']=='romulus-sanctuary')
im=Image.new('RGB',(1600,1130),'#101820');d=ImageDraw.Draw(im)
font=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',21);big=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',30)
def pt(x,z):return (50+x*3,1030-z*2.3)
for o in sorted(m['Objects'],key=lambda o: o['Kind']!='Terrain'):
 x,z=o['Position']['X'],o['Position']['Z'];a,b=pt(x,z);df=o.get('Def',{}).get('Name','')
 if o['Kind']=='Terrain':d.rectangle((a,b,a+3,b+2.3),fill='#243039' if x>150 else '#273b3e')
 if o['Kind']=='Thing':
  color='#d06c68' if o['Faction']=='Hostile' else '#9cb5b5'
  if df=='HiddenConduit':continue
  if 'Wall' in df:d.rectangle((a,b,a+3,b+2.3),fill=color)
  elif df=='PMP_RomulusDome':d.ellipse((a-120,b-92,a+120,b+92),outline='#934947',width=2)
  elif 'Turret' in df:d.rectangle((a-4,b-4,a+5,b+5),fill='#ffae77')
  else:d.rectangle((a-2,b-2,a+3,b+3),fill=color)
 if o['Kind']=='PawnKind' and o.get('SpawnAtStart',True):d.ellipse((a-3,b-3,a+3,b+3),fill='#ff6c64')
lookup={o['Id']:o for o in m['Objects']}
for prefix,color in [('mason','#ffdc6e'),('empire','#64d5e2')]:
 pts=[]
 for i in range(1,4):
  for j in range(5 if prefix=='mason' else 3):
   o=lookup[f'{prefix}-route-{i}-{j}'];pts.append(pt(o['Position']['X'],o['Position']['Z']))
 if prefix=='empire':pts.insert(0,pt(90,235))
 d.line(pts,fill=color,width=3)
 for a,b in pts:d.ellipse((a-4,b-4,a+4,b+4),fill=color)
for r in m['Rules']:
 if r.get('ShowOnMap') and r['Events'][0]['Kind']=='Periodic':
  a,b=pt(r['Position']['X'],r['Position']['Z']);d.rectangle((a-6,b-6,a+6,b+6),outline='#67e2c1' if 'empire' in r['Id'] else '#ee9286',width=3)
d.text((50,22),'第三关 · 西向东纵深攻坚 / 500 × 400',font=big,fill='white')
d.text((50,72),'青线：帝国主攻　黄线：梅森南部控制站　空心方块：30秒补员触发器',font=font,fill='#c6d3da')
for text,x,z in [('帝国营地',23,307),('哨卡一：20战犬＋10信徒',214,334),('哨卡二：25战犬＋2禁军',304,353),('哨卡三：40战犬＋7禁军',394,334)]:d.text(pt(x,z),text,font=font,fill='white')
d.text(pt(212,65),'南部街巷 · 依次操作控制站，等待对应哨卡被帝国占领',font=font,fill='#ffdc6e')
d.text((50,1080),'图像由实际作者工程生成；地图坐标北向上。红色为罗穆卢斯，灰青色为帝国设施。',font=font,fill='#a8bac7')
out=R/'artifacts/mission-three/west-east-rebuild/map-overview.png';im.save(out);print(out)

