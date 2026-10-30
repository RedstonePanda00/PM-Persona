from pathlib import Path
import hashlib, json, xml.etree.ElementTree as ET
root=Path(__file__).resolve().parents[2]
texts={
'第五关 · 钢之意志':'Mission 5 · Will of Steel','坚守':'Hold the Line',
'行动单位：前哨建设探机组':'Deployment: Outpost construction probes',
'地点：罗穆卢斯腹地 · 环形山谷':'Location: Romulus territory · Ring valley',
'坚守至盟友完成防御阵地':'Hold until the allied defensive positions are established',
'完成折跃撤离':'Complete warp extraction',
'前哨已经落地。利用这里的钢铁建立防线，把他们引过来。':'The outpost is in place. Use the local steel to build your defenses and draw them in.',
'盟友需要时间建立阵地。守住四个入口，我们会通知你什么时候可以撤离。':'Our allies need time to establish their defenses. Hold the four entrances. We will tell you when extraction is available.',
'盟友阵地已完成。现在可以撤离，也可以继续坚守。':'The allied defenses are ready. You may extract now or keep holding the line.',
'盟友已经站稳脚跟。正在执行群体召回。':'Our allies are secure. Executing group recall.',
'前哨已失守：复制器和探机全部损毁。':'The outpost has fallen: all replicators and probes have been destroyed.',
'左下':'Southwest','右上':'Northeast','左上':'Northwest','右下':'Southeast'}
keyed={
'PMP_M5_Remaining':('最低坚守剩余：{0}','Minimum hold remaining: {0}'),
'PMP_M5_Ready':('盟友阵地已完成，可以撤离','Allied defenses ready. Extraction available.'),
'PMP_M5_Kills':('击杀数：{0}','Kills: {0}'),
'PMP_M5_Attracted':('据点附近军团规模：{0}','Legion forces around the outpost: {0}'),
'PMP_M5_AttractedTip':('表示被前哨吸引的敌军规模，是挑战成绩，不是下一波实际生成数量。','A strategic attraction score, not the number of pawns in the next wave.'),
'PMP_M5_ShowOre':('显示钢铁深层矿区','Show the deep steel deposit'),
'PMP_M5_Extract':('撤退','Retreat'),
'PMP_M5_BuildHint':('采矿 → 入柜 → 复制与转化 → 建立防线','Mine → store → replicate and convert → defend'),
'PMP_M5_Wave':('{0}方向敌军正在推进（{1}）','Enemy forces advancing from the {0} ({1})'),
'PMP_M5_Result':('行动：坚守\n坚守时长：{0}\n击杀数：{1}\n吸引敌军规模：{2}\n帝国防线已就绪，现存部队与建筑已召回。行星碎裂炮首轮打击完成。','Operation: Hold the Line\nTime held: {0}\nKills: {1}\nEnemy forces attracted: {2}\nImperial defenses are ready. Surviving units and buildings have been recalled. The first Planet-Fracture salvo is complete.')}
texts.update({
'执行官，根据你的指示，我们已经通知了卡珊德拉骑士，她的部队会立刻以稳定状态的子节点为核心建立起防线。':'Executor, as you instructed, we have contacted Knight Cassandra. Her troops will immediately establish defenses around the stabilized subnode.',
'净化者会为您赴汤蹈火，执行官，我们找到了一处绝佳的地点，四面环山，附近有四处罗穆卢斯子节点。':'The Purifiers will brave any danger for you, Executor. We have found an ideal location, surrounded by mountains, with four Romulus subnodes nearby.',
'格式塔小队听从您的调遣指挥官，我们会负责引导军团战士，各小队已在子节点附近就绪，只要你一声令下我们就会发动袭击。':'Gestalt is at your command. We will draw the Legion troops toward you. Our teams are in position near the subnodes, ready to strike on your order.',
'Bravo-0-6就绪，我会负责东北方向，阿德勒和贝尔负责东南。':'Bravo-0-6 ready. I have the northeast. Adler and Bell have the southeast.',
'我和子墨会负责西北方向。':'Zimo and I have the northwest.',
'可不是吗老兄，顺便一提长官，卡玛和林薇已经去西南了。':'You said it, mate. By the way, sir, Karma and Wei Lin have already headed southwest.',
'袭击会非常凶猛，不过我们相信您的判断。我们拖延的时间越长，帝国对据点的控制就越牢固。':'The assault will be fierce, but we trust your judgment. The longer we hold them here, the stronger the Empire can make its positions.',
'梅森通话完毕。':'Mason out.',
'我们的时间不多执行官，敌人很快就会淹没我们所在的位置，在此之前我们要尽可能地拖延时间。':'We are short on time, Executor. The enemy will soon overrun our positions. Until then, we need to delay them as long as possible.',
'我们需要建立防线抵挡他们，好在这里的矿物储量丰富，我们可以用复制器转换一些资源。':'We need defenses to hold them back. There are rich mineral deposits here; we can convert resources with the replicator.',
'通话完毕。':'Out.',
'执行官，军团正在西南方向集结。':'Executor, the Legion is gathering to the southwest.',
'敌人扑过来了，战士们，亮出光刃！':'The enemy is upon us! Warriors, ready your blades!',
'指挥官，军团已经抵达西北方向，准备冲击我们的基地。':'Commander, the Legion has reached the northwest and is preparing to assault our base.',
'执行官，东南方向检测到军团的战士。':'Executor, Legion troops detected to the southeast.',
'军团的人上钩了长官，他们在基地的东北方向集结。':'The Legion took the bait, sir. They are gathering northeast of the base.',
'你们无法抵抗军神的怒火！':'You cannot withstand the wrath of the god of war!',
'我们 会碾碎你们脆弱的防线.':'We will crush your fragile defenses.',
'你们徒劳的抵抗能得到的只有灭亡。':'Your futile resistance will bring you only annihilation.',
'为了军神的荣耀，冲锋！':'For the glory of the god of war! Charge!',
'这可不妙...罗穆卢斯派出了他的冠军勇士。':'This is not good... Romulus has sent his champion.',
'敌人派出了一名冠军勇士。':'The enemy has deployed a champion warrior.',
'执行官，这里是Bravo-0-6，一名冠军勇士朝我们的基地来了。':'Executor, Bravo-0-6 here. A champion warrior is heading for our base.',
'指挥官，一名冠军勇士正在接近我方基地。':'Commander, a champion warrior is approaching our base.',
'长官，冠军勇士来了。':'Sir, a champion warrior is coming.',
'感谢你们的支援，执行官，我们已经搭建起了初步的防线，工程进度顺利。':'Thank you for your support, Executor. Our initial defenses are in place. Construction is proceeding smoothly.',
'重型炮台和结构墙已经加固完毕，就快好了，执行官。':'The heavy turrets and structural walls have been reinforced. We are almost ready, Executor.',
'物流系统搭建完毕，就快好了，执行官，只要一小会儿。':'The logistics system is ready. Almost done, Executor. Just a little longer.',
'据点已就绪，执行官，你们随时可以撤离，感谢你们的支援。':'Our positions are ready, Executor. You may withdraw at any time. Thank you for your support.',
'你们听到命令了，战士们，执行群体召回！':'You heard the order, warriors! Execute mass recall!',
'西南':'Southwest','西北':'Northwest','东南':'Southeast','东北':'Northeast',
'卡珊德拉':'Cassandra','信徒':'Believer','禁军':'Legion Guard','神选者':'Legion Chosen','百夫长':'Centurion'})
for lang in ['English','ChineseSimplified (简体中文)']:
    folder=root/'Languages'/lang/'Keyed';folder.mkdir(parents=True,exist_ok=True)
    existing={e.tag for f in folder.glob('*.xml') if f.name!='MissionFive.xml' for e in ET.fromstring(f.read_text(encoding='utf-8-sig'))}
    xml=ET.Element('LanguageData')
    for zh,en in texts.items():
        key='RC_Text_'+hashlib.sha256(zh.encode()).hexdigest()[:16]
        if key not in existing: ET.SubElement(xml,key).text=en if lang=='English' else zh
    for key,(zh,en) in keyed.items(): ET.SubElement(xml,key).text=en if lang=='English' else zh
    ET.indent(xml,space='  ');ET.ElementTree(xml).write(folder/'MissionFive.xml',encoding='utf-8',xml_declaration=True)
print('Mission-five localization written.')
