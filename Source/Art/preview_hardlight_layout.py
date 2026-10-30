"""Contact sheets from native GunSmith output, for inspection only."""
from pathlib import Path
from PIL import Image, ImageDraw

root = Path(__file__).resolve().parents[2]
run = Path((root / 'artifacts/hardlight-layout/latest-audit.txt').read_text(encoding='utf-8-sig').strip())
folder = run / 'HardlightArtAudit'
names = ['bare', 'PMP_HardlightRifle_Optic', 'PM_Common_MarksmanOptic', 'PM_Common_ViperSight',
         'PM_Common_UB_FrontGrip01', 'PM_Common_UB_JetGrip', 'PM_Common_UB_Launcher_HE',
         'PM_Common_UB_Launcher_Shotgun', 'PM_Common_UB_Launcher_LaserCannon']
for output, entries in [('assembly-preview', names), ('combined-preview', sorted(p.stem for p in folder.glob('*__*.png')))]:
    sheet = Image.new('RGB', (1440, ((len(entries)+2)//3)*280), (48,51,57))
    draw = ImageDraw.Draw(sheet)
    for i, name in enumerate(entries):
        tile = Image.open(folder/(name+'.png')).crop((260,125,760,400)).convert('RGBa').resize((460,253),Image.Resampling.LANCZOS).convert('RGBA')
        x,y=(i%3)*480,(i//3)*280
        sheet.paste(tile,(x+10,y+22),tile.getchannel('A'))
        for row,label in enumerate(name.split('__')):
            draw.text((x+10,y+2+row*12),label,fill='white')
    sheet.save(root/'artifacts/hardlight-layout'/(output+'.png'))
