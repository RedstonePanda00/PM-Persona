"""Fit ImageGen sprites without distortion; generate circular black borders at 4x resolution."""
from pathlib import Path
from PIL import Image, ImageChops, ImageFilter, ImageDraw, ImageFont
import shutil
import numpy as np
from sprite_edges import clean_cutout

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'artifacts/romulus-structures-20261025'
OUT.mkdir(exist_ok=True)
GEN=Path('C:/Users/15858/.codex/generated_images/01a16c3e-5e57-7a73-a08c-8e10a8872f8c')
sources={'Dome':'exec-b8abcfc9-5d27-46c4-a5b3-ee0513c2e20e.png','Hub':'exec-3522b6b7-6263-457f-ab7b-8d355c06123a.png'}
board=Image.new('RGBA',(1100,760),(47,50,53,255))
for index,(name,filename) in enumerate(sources.items()):
    shutil.copy2(GEN/filename,OUT/(name+'-imagegen.png'))
    raw=Image.open(OUT/(name+'-imagegen.png')).convert('RGBA')
    raw=clean_cutout(raw,trim=1)
    raw=raw.crop(raw.getbbox())
    size=(512,512) if name=='Dome' else (512,640)
    scale=4
    fit=min((size[0]-64)*scale/raw.width,(size[1]-64)*scale/raw.height)
    raw=raw.resize((round(raw.width*fit),round(raw.height*fit)),Image.Resampling.LANCZOS)
    high=Image.new('RGBA',(size[0]*scale,size[1]*scale))
    high.alpha_composite(raw,((high.width-raw.width)//2,(high.height-raw.height)//2))
    # Circular dilation creates uniform padding rather than square-corner dilation.
    alpha=np.asarray(high.getchannel('A'))>=128
    radius=14;expanded=np.zeros_like(alpha);padded=np.pad(alpha,radius)
    for dy in range(-radius,radius+1):
        for dx in range(-radius,radius+1):
            if dx*dx+dy*dy<=radius*radius:
                expanded |= padded[radius+dy:radius+dy+high.height,radius+dx:radius+dx+high.width]
    outline=Image.new('RGBA',high.size,(0,0,0,0))
    outline.putalpha(Image.fromarray(expanded.astype('uint8')*255))
    outline.alpha_composite(high)
    final=outline.resize(size,Image.Resampling.LANCZOS)
    final.save(ROOT/f'Textures/Things/Building/Romulus/{name}.png')
    board.alpha_composite(final,(index*550+(550-size[0])//2,50))
    ImageDraw.Draw(board).text((index*550+275,710),name,anchor='mm',fill='white',font=ImageFont.truetype('C:/Windows/Fonts/arial.ttf',27))
    print(name,final.size,'alpha',final.getchannel('A').getextrema())
board.convert('RGB').save(OUT/'review.png')
