"""Prepare Z-390's 1024px body; native texture ratios size shared optics."""
from pathlib import Path
import shutil
import numpy as np
from PIL import Image
from sprite_edges import clean_cutout

root = Path(__file__).resolve().parents[2]
raw = root / 'artifacts/incineration-cannon/IncinerationCannon-imagegen.png'
if not raw.exists():
    shutil.copy2(Path(r'C:\Users\15858\.codex\generated_images\01a175ee-268a-7aa0-9016-f515a99cc55a\exec-a2a6489a-5a93-429d-a9e0-b984941e0698.png'), raw)
sprite = clean_cutout(Image.open(raw).convert('RGBA'), trim=3)
sprite = sprite.crop(sprite.getchannel('A').getbbox())
scale = min(1840 / sprite.width, 880 / sprite.height)
sprite = sprite.resize(tuple(round(v * scale) for v in sprite.size), Image.Resampling.LANCZOS)
canvas = Image.new('RGBA', (2048, 1024))
canvas.alpha_composite(sprite, ((2048-sprite.width)//2, (1024-sprite.height)//2))
# GunSmith sizes a part by part-source-width / body-source-width. Keep the
# silhouette's canvas occupancy and world drawSize while halving relative optics.
result = canvas.convert('RGBa').resize((1024,512), Image.Resampling.LANCZOS).convert('RGBA')
pixels = np.array(result)
pixels[pixels[:,:,3] < 4] = 0
target = root / 'Textures/Things/Weapon/IncinerationCannon/IncinerationCannon.png'
target.parent.mkdir(parents=True, exist_ok=True)
Image.fromarray(pixels).save(target)
print(target)

