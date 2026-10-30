from pathlib import Path
import hashlib,xml.etree.ElementTree as ET
root=Path(__file__).resolve().parents[2]
texts={
'拔线':'Severance',
'第四关 · 拔线':'Mission 4 · Severance',
'地点：罗穆卢斯枢纽子节点':'Location: Romulus hub subnode',
'安装现实稳定锚，压制枢纽':'Install the reality anchor and suppress the hub',
'完成原地群体召回':'Complete the group recall from current positions',
'自动安装剩余秒数':'Automatic installation: seconds remaining',
'安装现实稳定锚':'Install reality anchor',
'启动自动安装；随后可自由战斗，约 60 秒后完成。':'Start automatic installation. You can move and fight while it completes in about 60 seconds.',
'现实稳定锚：节点已压制':'Reality anchor: node suppressed',
'现实稳定锚：尚未安装':'Reality anchor: not installed',
'梅森已失去战斗能力。':'Mason is no longer combat-capable.',
'稳定锚已安装，节点已压制。梅森及本关仍在场的增援已折跃返回曙光号。':'The reality anchor is installed and the node suppressed. Mason and the surviving reinforcements have warped back aboard the Aurora.',
'战士，已经苏醒.':'The warriors have awakened.',
'欢迎加入，战士们。':'Welcome aboard, warriors.',
'我重回战场.':'I return to the battlefield.',
'En taro Artanis，恺撒，我的荣幸。':'En taro Artanis. Caesar, the honor is mine.',
'我来助你一臂之力，梅森.':'I am here to lend you my strength, Mason.',
'很高兴见到你，卡尔达利斯.':'Good to see you, Kaldalis.',
'敌人将在我的战刃之下陨落.':'Our enemies will fall beneath my blades.',
'曙光号这里是梅森，我已抵达目标位置。':'Aurora, this is Mason. I have reached the target location.',
'收到，保持警惕，梅森。':'Copy. Stay alert, Mason.',
'正面强攻并不理智。':'A frontal assault would be unwise.',
'看起来是一处维护通道。':'Looks like a maintenance passage.',
'正适合潜入。':'Perfect for slipping inside.',
'梅森，我们的战士渴望敌人的鲜血，端掉那座哨站，我们就能投送一些增援来帮你。':'Mason, our warriors thirst for enemy blood. Take out that outpost and we can send reinforcements to aid you.',
'另一处哨站，梅森，端掉他，我们会派一些战士来帮你。':'Another outpost, Mason. Destroy it and we will send warriors to assist you.',
'那是最后一处哨站了，梅森。':'That is the last outpost, Mason.',
'开始安装现实稳定锚。':'Starting reality-anchor installation.',
'敌人扑过来了，圣堂武士，准备战斗。':'The enemy is upon us. Templar, prepare for battle.',
'发现增援。它们正往核心靠近。':'Reinforcements spotted. They are moving toward the core.',
'只剩最后一段了，梅森。':'Almost there, Mason.',
'安装完成，快，折跃上船！':'Installation complete. Hurry, warp back aboard!',
'我们已经锁定你的位置，正在执行群体召回。':'We have a lock on your positions. Executing group recall.',
'警戒者':'Sentinel','不朽者':'Immortal',
}
for i in range(1,4):texts[f'摧毁第 {i} 哨所方尖碑并清除守军（可选）']=f'Destroy outpost {i}\'s obelisk and clear its defenders (optional)'
for language in ['English','ChineseSimplified (简体中文)']:
 folder=root/'Languages'/language/'Keyed';folder.mkdir(parents=True,exist_ok=True)
 xml=ET.Element('LanguageData')
 existing={e.tag for file in folder.glob('*.xml') if file.name!='MissionFour.xml' for e in ET.parse(file).getroot()}
 for zh,en in texts.items():
  key='RC_Text_'+hashlib.sha256(zh.encode()).hexdigest()[:16]
  if key not in existing:ET.SubElement(xml,key).text=en if language=='English' else zh
 ET.indent(xml,space='  ');ET.ElementTree(xml).write(folder/'MissionFour.xml',encoding='utf-8',xml_declaration=True)
folder=root/'Languages/ChineseSimplified (简体中文)/DefInjected/ThingDef';folder.mkdir(parents=True,exist_ok=True)
xml=ET.Element('LanguageData');ET.SubElement(xml,'PMP_RomulusSubnode.label').text='罗穆卢斯枢纽';ET.SubElement(xml,'PMP_RomulusSubnode.description').text='一座高耸，嗡鸣着的超凡科技结构，常规武器无法对其造成伤害。这座结构是超凡智能 [罗穆卢斯] 的一个子节点。'
ET.indent(xml,space='  ');ET.ElementTree(xml).write(folder/'MissionFour.xml',encoding='utf-8',xml_declaration=True)
print('Mission-four English and Chinese localization written.')
