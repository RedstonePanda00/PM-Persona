"""Review native GunSmith composites at the same scale; not a shipped texture."""
from pathlib import Path
from PIL import Image, ImageDraw

root = Path(__file__).resolve().parents[2]
run = Path((root / 'artifacts/binary-rifle/latest-audit.txt').read_text(encoding='utf-8-sig').strip())
names = ['bare', 'PMP_HardlightRifle_Optic', 'PM_Common_MarksmanOptic', 'PM_Common_ViperSight']
sheet = Image.new('RGB', (720, 1120), (48, 51, 57))
draw = ImageDraw.Draw(sheet)
for index, name in enumerate(names):
    sprite = Image.open(run / 'BinaryRifleAudit' / (name + '.png')).crop((280, 120, 745, 300))
    sprite = sprite.convert('RGBa').resize((660, 255), Image.Resampling.LANCZOS).convert('RGBA')
    sheet.paste(sprite, (30, index * 280 + 22), sprite.getchannel('A'))
    draw.text((30, index * 280 + 10), name, fill='white')
sheet.save(root / 'artifacts/binary-rifle/assembly-preview.png')
