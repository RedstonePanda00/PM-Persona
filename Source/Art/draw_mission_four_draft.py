from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
ROOT=Path(__file__).resolve().parents[2]
out=ROOT/'Docs/Images/mission-four-map-draft.png'
im=Image.new('RGB',(1840,1260),'#20292c');d=ImageDraw.Draw(im)
font=lambda n:ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',n)
d.text((72,35),'第四关 · 拔线',font=font(42),fill='#f3e8cf')
d.text((74,94),'梅森单人出发 · 战斗推进  /  300 × 240 格  /  北 ↑  /  2026-10-25',font=font(23),fill='#aabdbb')
ox,oy,s=76,160,4
def pt(x,z):return (ox+x*s,oy+(240-z)*s)
def box(x0,z0,x1,z1,fill,outline=None,width=2):d.rectangle((pt(x0,z1),pt(x1,z0)),fill=fill,outline=outline,width=width)
def line(points,fill,width=5):d.line([pt(*p) for p in points],fill=fill,width=width,joint='curve')
def label(x,z,text,color='#f0ece1',size=23):d.text(pt(x,z),text,fill=color,font=font(size),anchor='mm')
box(0,0,300,240,'#384443','#718280')
for x in range(0,301,25):line([(x,0),(x,240)],'#43504e',1)
for z in range(0,241,25):line([(0,z),(300,z)],'#43504e',1)
for x in range(0,301,50):d.text((pt(x,0)[0],1140),str(x),font=font(18),fill='#aabdbb',anchor='mt')
for z in range(0,241,40):d.text((62,pt(0,z)[1]),str(z),font=font(18),fill='#aabdbb',anchor='rm')
for polygon in [[(0,0),(75,0),(95,45),(70,65),(22,95),(0,100)],[(300,0),(220,0),(205,45),(242,62),(283,109),(300,110)],[(0,160),(45,173),(65,220),(55,240),(0,240)],[(265,160),(300,146),(300,240),(240,240)]]:
 d.polygon([pt(*p) for p in polygon],fill='#57625a',outline='#7b8170')
label(40,28,'岩坡',size=22);label(266,31,'岩坡',size=22)
box(87,80,215,215,'#44423d','#c3b9a1',9)
box(90,85,212,212,'#454940')
box(32,60,83,153,'#36534d','#70b9aa',3)
box(215,70,265,148,'#63483f','#d69478',3)
box(96,112,133,157,'#655c3c','#dfbe6e',3)
box(135,157,190,208,'#5c464a','#d0919d',4)
box(122,15,180,44,'#385763','#80c6da',3)
box(103,220,127,236,'#633e3e','#ce7c75',3);box(200,220,224,236,'#633e3e','#ce7c75',3)
# Open junctions, not a maze.
box(80,122,96,136,'#454940');box(208,112,220,128,'#454940')
box(108,208,122,218,'#454940');box(203,208,215,218,'#454940')
for x,z in [(224,98),(253,119),(147,164),(182,181),(110,196)]:
 box(x-4,z-2,x+4,z+2,'#d2c5ab','#241c1b',2)
for x,z in [(224,108),(252,132),(184,198)]:
 d.ellipse((pt(x-3,z+3),pt(x+3,z-3)),fill='#c7584f',outline='#f0b09c',width=2)
line([(146,41),(110,55),(59,63),(60,122),(112,130),(147,145),(160,173)],'#75d6c0',7)
line([(160,42),(216,56),(241,76),(239,120),(202,120),(178,143),(169,172)],'#e6ab7b',7)
line([(115,220),(115,190),(144,183)],'#e57979',5)
line([(212,220),(207,185),(187,180)],'#e57979',5)
for x,z in [(60,100),(136,140),(239,99),(195,127),(115,204),(207,200)]:
 d.ellipse((pt(x-1.6,z+1.6),pt(x+1.6,z-1.6)),fill='#eee9d6')
# Three optional outposts beside the approach routes; each has a reserved warp area.
for x0,z0,x1,z1,cx,cz,name in [(130,50,174,76,152,63,'S1 第一哨所'),(145,101,177,132,161,116,'S2 第二哨所'),(222,166,254,198,238,183,'S3 第三哨所')]:
 box(x0,z0,x1,z1,'#594766','#cda3ec',3)
 label(cx,cz,name,size=17)
line([(110,55),(130,63)],'#cda3ec',4)
line([(174,63),(216,56)],'#cda3ec',4)
line([(161,132),(161,145)],'#cda3ec',4)
line([(147,145),(161,145),(178,143)],'#cda3ec',4)
box(211,177,219,185,'#454940')
line([(190,181),(222,181)],'#cda3ec',4)
label(151,30,'A 单人进入',size=20)
label(57,147,'B 维护通道',size=20)
label(241,143,'C 正门',size=21)
label(114,151,'D 技术书库',size=18)
label(164,202,'E 枢纽庭院',size=19)
d.ellipse((pt(157,190),pt(175,172)),fill='#d0c5ab',outline='#e3aa67',width=4)
label(166,181,'枢纽',color='#733d39',size=18)
label(115,228,'F',size=23);label(212,228,'F',size=23)
tx=1330
d.text((tx,175),'流程',font=font(31),fill='#f3e8cf')
for i,(heading,body) in enumerate([
 ('01 单人进入','梅森行动，南部仅为进入点'),('02 战斗推进','正门交战或维护通道绕侧'),('03 可选哨站','摧毁方尖碑并清除守军'),('04 启动稳定锚','右键 / Gizmo 选 Pawn 短交互'),('05 自动安装防守','约 60 秒，梅森可自由战斗'),('06 完成即群体召回','每个友方 Pawn 原地折跃上船')]):
 y=230+i*76;d.text((tx,y),heading,font=font(25),fill='#ead7b4');d.text((tx,y+40),body,font=font(20),fill='#bbcbc7')
d.text((tx,718),'支线增援 · 每处水晶塔 × 1',font=font(25),fill='#e3c6f6')
for i,(title,body) in enumerate([
 ('S1  警戒者 × 3 ＋ 机械哨兵 × 1','机械哨兵携带硬光步枪'),
 ('S2  不朽者 × 1','“En taro Artanis，恺撒”'),
 ('S3  卡尔达利斯','“我来助你一臂之力，梅森”')]):
 y=773+i*88;d.text((tx,y),title,font=font(21),fill='#ead7b4');d.text((tx,y+34),body,font=font(19),fill='#bbcbc7')
d.text((tx,1048),'方尖碑摧毁 ＋ 守军清除 → 增援',font=font(20),fill='#e3c6f6')
d.text((tx,1080),'建筑折跃 → 单位折跃 → 默认征召',font=font(20),fill='#e3c6f6')
for i,(color,text) in enumerate([('#75d6c0','西侧路线'),('#e6ab7b','正门路线'),('#e57979','敌军'),('#cda3ec','支线')]):
 x=tx+(i%2)*210;y=1130+(i//2)*32;d.line((x,y+11,x+30,y+11),fill=color,width=5);d.text((x+42,y),text,font=font(18),fill='#cfdbd6')
d.text((76,1195),'首版已实装，布局可继续细化。主线枢纽无生命值；安装完成后各单位原地召回。',font=font(23),fill='#c8d4ce')
im.save(out)
print(out)
