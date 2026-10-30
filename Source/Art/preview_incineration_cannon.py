"""Review native GunSmith composites at the same scale; not a shipped texture."""
from pathlib import Path
from PIL import Image, ImageDraw

root = Path(__file__).resolve().parents[2]
run = Path((root / 'artifacts/incineration-cannon/latest-audit.txt').read_text(encoding='utf-8-sig').strip())
names = ['bare', 'PM_Common_UB_FrontGrip01', 'PM_Common_UB_JetGrip', 'PM_Common_UB_Launcher_HE', 'PM_Common_UB_Launcher_Shotgun', 'PM_Common_UB_Launcher_LaserCannon']
sheet = Image.new('RGB', (1440, 1290), (48, 51, 57))
draw = ImageDraw.Draw(sheet)
for index, name in enumerate(names):
    sprite = Image.open(run / 'IncinerationAudit' / (name + '.png')).crop((275, 130, 750, 410))
    sprite = sprite.convert('RGBa').resize((660, 389), Image.Resampling.LANCZOS).convert('RGBA')
    x,y=(index%2)*720,(index//2)*430
    sheet.paste(sprite, (x+30, y+22), sprite.getchannel('A'))
    draw.text((x+30, y+10), name, fill='white')
sheet.save(root / 'artifacts/incineration-cannon/assembly-preview.png')

