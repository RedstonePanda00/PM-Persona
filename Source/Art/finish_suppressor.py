"""Prepare the Z-130 body without an authored outline or a separate preview."""
from pathlib import Path
import shutil
import numpy as np
from PIL import Image, ImageFilter, ImageOps
from sprite_edges import clean_cutout

root = Path(__file__).resolve().parents[2]
raw = root / 'artifacts/suppressor/Suppressor-imagegen.png'
if not raw.exists():
    shutil.copy2(Path(r'C:\Users\15858\.codex\generated_images\01a175ee-268a-7aa0-9016-f515a99cc55a\exec-826f08a0-9300-4247-b31b-ab74b343a258.png'), raw)
sprite = clean_cutout(Image.open(raw).convert('RGBA'), trim=3)
sprite = sprite.crop(sprite.getchannel('A').getbbox())
scale = min(1840 / sprite.width, 880 / sprite.height)
sprite = sprite.resize(tuple(round(v * scale) for v in sprite.size), Image.Resampling.LANCZOS)
canvas = Image.new('RGBA', (2048, 1024))
canvas.alpha_composite(sprite, ((2048-sprite.width)//2, (1024-sprite.height)//2))
result = canvas.convert('RGBa').resize((512,256), Image.Resampling.LANCZOS).convert('RGBA')
pixels = np.array(result)
pixels[pixels[:,:,3] < 4] = 0
target = root / 'Textures/Things/Weapon/Suppressor/Suppressor.png'
target.parent.mkdir(parents=True, exist_ok=True)
Image.fromarray(pixels).save(target)
# Crossed white silhouettes match the B9/B25 dual-wield attachment icon language.
mask = Image.fromarray(pixels).getchannel('A')
mask = mask.crop(mask.getbbox()).resize((620,290), Image.Resampling.LANCZOS)
icon = Image.new('RGBA', (1024,1024))
for layer in (mask.rotate(-45, Image.Resampling.BICUBIC, expand=True),
              ImageOps.mirror(mask).rotate(45, Image.Resampling.BICUBIC, expand=True)):
    tile = Image.new('RGBA', layer.size, (64,64,64,0))
    tile.putalpha(layer.filter(ImageFilter.MaxFilter(25)))
    white = Image.new('RGBA', layer.size, (255,255,255,0))
    white.putalpha(layer)
    tile.alpha_composite(white)
    icon.alpha_composite(tile, ((1024-tile.width)//2, (1024-tile.height)//2))
icon.convert('RGBa').resize((512,512), Image.Resampling.LANCZOS).convert('RGBA').save(
    target.with_name('Suppressor_DualGrip_Icon.png'))
print(target)
