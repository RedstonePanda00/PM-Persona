"""Prepare the Z-110 body canvas; GunSmith supplies the final outline."""
from pathlib import Path
import shutil
import numpy as np
from PIL import Image
from sprite_edges import clean_cutout

root = Path(__file__).resolve().parents[2]
raw = root / 'artifacts/boltshot/Boltshot-matte-imagegen.png'
if not raw.exists():
    shutil.copy2(Path(r'C:\Users\15858\.codex\generated_images\01a175ee-268a-7aa0-9016-f515a99cc55a\exec-eb1528aa-582f-4115-85f6-5d686b6a6252.png'), raw)
source = clean_cutout(Image.open(raw).convert('RGBA'), trim=3)
assert source.getchannel('A').getextrema() == (0, 255)
sprite = source.crop(source.getchannel('A').getbbox())
scale = min(1840 / sprite.width, 900 / sprite.height)
sprite = sprite.resize(tuple(round(v * scale) for v in sprite.size), Image.Resampling.LANCZOS)
canvas = Image.new('RGBA', (2048, 1024))
canvas.alpha_composite(sprite, ((2048-sprite.width)//2, (1024-sprite.height)//2))
# Native attachment sizing uses part pixels / body pixels, as on B9.
# Keep the body at 512 pixels so shared optics retain their natural size.
result = canvas.convert('RGBa').resize((512,256),Image.Resampling.LANCZOS).convert('RGBA')
pixels=np.array(result);pixels[pixels[:,:,3]==0]=0
target=root/'Textures/Things/Weapon/Boltshot/Boltshot.png';target.parent.mkdir(parents=True,exist_ok=True)
Image.fromarray(pixels).save(target)
print(target)
