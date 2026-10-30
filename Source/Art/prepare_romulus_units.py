from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
import shutil
from sprite_edges import clean_cutout, pawn_outline

ROOT=Path(__file__).resolve().parents[2]
ART=ROOT/'artifacts/romulus-unit-concepts'
OUT=ROOT/'artifacts/romulus-units-release'
OUT.mkdir(exist_ok=True)
GEN=Path('C:/Users/15858/.codex/generated_images/01a159bf-c908-7af0-a961-a050d092a3df')
sources=OUT/'source'; sources.mkdir(exist_ok=True)
for name,file in [('hound-sheet','exec-22686c16-87a9-4971-9334-557676bbfe7e.png'),('chosen-east-north','exec-40e523e3-7160-40ce-8051-e5301abc1a2c.png')]:
    if not (sources/(name+'.png')).exists():shutil.copy2(GEN/file,sources/(name+'.png'))

def fit(raw, extent=450, rotate=0, trim=2):
    im=clean_cutout(raw,trim)
    im=im.crop(im.getbbox())
    if rotate:im=im.rotate(rotate,expand=True)
    im.thumbnail((extent,extent),Image.Resampling.LANCZOS)
    canvas=Image.new('RGBA',(512,512))
    canvas.alpha_composite(im,((512-im.width)//2,(512-im.height)//2))
    return pawn_outline(canvas,3)

hound=Image.open(sources/'hound-sheet.png').convert('RGBA')
chosen=Image.open(sources/'chosen-east-north.png').convert('RGBA')
groups={
 'Warhound': [hound.crop((0,0,440,724)),hound.crop((440,0,1760,724)),hound.crop((1760,0,2172,724))],
 'LegionGuard':[Image.open(ART/f'legion-praetorian-three-view-v1/source/{d}.png').convert('RGBA') for d in ['south','east','north']],
 'LegionChosen':[Image.open(ART/'superheavy-south-v1/Superheavy_south_raw.png').convert('RGBA'),chosen.crop((0,0,887,887)),chosen.crop((887,0,1774,887))]
}
board=Image.new('RGBA',(1536,1536),(40,43,46,255))
draw=ImageDraw.Draw(board); font=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',22)
for row,(name,views) in enumerate(groups.items()):
    folder=ROOT/'Textures/Things/Pawn/Romulus';folder.mkdir(parents=True,exist_ok=True)
    for col,(direction,raw) in enumerate(zip(['south','east','north'],views)):
        result=fit(raw,450,trim=3 if name=='Warhound' else 2)
        result.save(folder/f'{name}_{direction}.png')
        board.alpha_composite(result,(col*512,row*512))
        draw.text((col*512+256,row*512+490),name+' / '+direction,font=font,anchor='mm',fill='white')
folder=ROOT/'Textures/Things/Equipment/Romulus';folder.mkdir(parents=True,exist_ok=True)
fit(Image.open(ART/'heavy-wolf-guard-v2-spear-raw.png').convert('RGBA'),460,-90).save(folder/'LegionSpear.png')
fit(Image.open(ART/'heavy-wolf-guard-v2-shield-raw.png').convert('RGBA'),450).save(folder/'LegionShield.png')
board.convert('RGB').save(OUT/'unit-directions-review.png')
print('Prepared 9 pawn sprites, spear and independent belt shield. All outer borders are script-generated pure black.')
