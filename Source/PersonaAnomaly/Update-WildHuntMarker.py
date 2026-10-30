"""Update the reusable base-mod marker in WildHunt source, preserving XML layout."""
from pathlib import Path
import re, shutil, datetime, xml.etree.ElementTree as ET
root=Path(r'C:\Users\15858\OneDrive\Documents\Sundry\RimworldMod\ProtossMechCore')
backup=Path(__file__).resolve().parents[2]/'artifacts/stasis'/('wildhunt-'+datetime.datetime.now().strftime('%Y%m%d-%H%M%S'))
changed=[]
parents={}
for folder in (Path(r'C:\Program Files (x86)\Steam\steamapps\common\RimWorld\Data\Core\Defs'),root/'1.7/Defs'):
    for path in folder.rglob('*.xml'):
        try:
            for node in ET.parse(path).getroot().findall('ThingDef'):
                if node.get('Name'):parents[node.get('Name')]=node
        except ET.ParseError:pass
def is_building(node,seen=None):
    seen=set() if seen is None else seen
    if node.find('category') is not None:return node.findtext('category')=='Building'
    parent=node.get('ParentName')
    if parent in parents and parent not in seen:return is_building(parents[parent],seen|{parent})
    return node.findtext('thingClass','').startswith('Building') or node.find('building') is not None
def write(path,text):
    old=path.read_text(encoding='utf-8-sig')
    if old==text:return
    saved=backup/path.relative_to(root);saved.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(path,saved)
    path.write_text(text,encoding='utf-8');changed.append(str(path))
source=root/'Source/1.7/ProtossMech/CompIgnoreSolarFlareElectricity.cs'
text=source.read_text(encoding='utf-8-sig')
if 'class CompProtossBuilding' not in text:
    insertion='''    /// Shared Protoss-building identifier. Extensions opt in through XML.
    /// Inherits the existing flare exemption; the base mod never references addons.
    public class CompProperties_ProtossBuilding : CompProperties
    {
        public CompProperties_ProtossBuilding() { compClass = typeof(CompProtossBuilding); }
    }
    public class CompProtossBuilding : CompIgnoreSolarFlareElectricity { }

'''
    text=text.replace('    [HarmonyPatch(typeof(PowerNet)',insertion+'    [HarmonyPatch(typeof(PowerNet)',1)
# Do not manufacture power when a real supply failure coincides with a flare.
if 'availableEnergy' not in text:
    text=text.replace('            for (int i = 0;', '            float availableEnergy = __instance.CurrentStoredEnergy() + __instance.CurrentEnergyGainRate();\n            for (int i = 0;',1)
    text=text.replace('                pt.PowerOn = true;', '''                if (!pt.PowerOn && availableEnergy + pt.EnergyOutputPerTick >= -0.0000001f)
                {
                    availableEnergy += pt.EnergyOutputPerTick;
                    pt.PowerOn = true;
                }''',1)
write(source,text)
source=root/'Source/1.7/ProtossMech/CompSolariteBatteryStorage.cs'
text=source.read_text(encoding='utf-8-sig')
text=text.replace('if (parent.Map != null && parent.Map.gameConditionManager.ElectricityDisabled(parent.Map))',
                  'if (parent.Map != null && parent.Map.gameConditionManager.ElectricityDisabled(parent.Map)\n                && parent.TryGetComp<CompProtossBuilding>() == null)')
write(source,text)
for path in (root/'1.7/Defs/ThingDefs_Structure').glob('*.xml'):
    text=path.read_text(encoding='utf-8-sig')
    def patch(match):
        block=match.group()
        if '<defName>' not in block:return block
        if not is_building(ET.fromstring(block)):
            block=re.sub(r'\s*<li Class="ProtossMech.CompProperties_ProtossBuilding"\s*/>','',block)
            return re.sub(r'\s*<comps>\s*</comps>','',block)
        block=block.replace('ProtossMech.CompProperties_IgnoreSolarFlareElectricity','ProtossMech.CompProperties_ProtossBuilding')
        if 'ProtossMech.CompProperties_ProtossBuilding' in block:return block
        marker='<li Class="ProtossMech.CompProperties_ProtossBuilding"/>'
        if '<comps>' in block:return block.replace('<comps>','<comps>\n      '+marker,1)
        if re.search(r'<comps\s+[^>]*>',block):return re.sub(r'(<comps\s+[^>]*>)',r'\1\n      '+marker,block,count=1)
        return block.replace('</ThingDef>','  <comps>'+marker+'</comps>\n  </ThingDef>')
    write(path,re.sub(r'<ThingDef\b[^>]*>.*?</ThingDef>',patch,text,flags=re.S))
backup.mkdir(parents=True,exist_ok=True)
(backup/'changed-files.txt').write_text('\n'.join(changed),encoding='utf-8')
print('\n'.join(changed))
