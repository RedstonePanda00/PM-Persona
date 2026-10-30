"""Clean authored layers; leave the runtime weapon outline to GunSmith."""
from pathlib import Path
import shutil
import numpy as np
from PIL import Image, ImageFilter, ImageOps
from sprite_edges import clean_cutout

ROOT = Path(__file__).resolve().parents[2]
TEX = ROOT / 'Textures/Things/Weapon'
RAW = ROOT / 'artifacts/gunsmith-cleanup/source'
RAW.mkdir(parents=True, exist_ok=True)

for name in ('Hardlight_Body', 'Hardlight_UnderbarrelMount'):
    target = TEX / 'HardlightRifle' / (name + '.png')
    source = RAW / (name + '.png')
    if not source.exists():
        shutil.copy2(target, source)
    im = Image.open(source).convert('RGBA')
    if name == 'Hardlight_Body':
        # The muzzle reflection is painted into opaque pixels, not an alpha seam.
        # Replace its short neutral highlight with the adjacent charcoal surface.
        pixels = np.array(im)
        region = pixels[267:275, 64:139]
        rgb = region[:, :, :3].astype(int)
        highlight = (rgb.min(axis=2) > 105) & (np.ptp(rgb, axis=2) < 65)
        fill = np.broadcast_to(pixels[276:277, 64:139, :3], rgb.shape)
        region[:, :, :3][highlight] = fill[highlight]
        im = Image.fromarray(pixels)
    im = clean_cutout(im, trim=1 if name == 'Hardlight_Body' else 0)
    # The rifle's source width sets shared attachment proportions without part
    # overrides. Keep the original 512px armor layer for Z-110's existing layout.
    outputs = [(target, (1024, 512))] if name == 'Hardlight_Body' else [
        (target, (512, 256)),
        (target.with_name('Hardlight_UnderbarrelMount_Rifle.png'), (1024, 512))]
    for output, size in outputs:
        scaled = im.convert('RGBa').resize(size, Image.Resampling.LANCZOS).convert('RGBA')
        pixels = np.array(scaled)
        pixels[pixels[:, :, 3] == 0] = 0
        Image.fromarray(pixels).save(output)

# Match B9/B25's crossed white weapon silhouettes on a dark gray icon border.
# This is a UI symbol only; its border is never baked onto the carried gun.
body = Image.open(TEX / 'Boltshot/Boltshot.png').convert('RGBA')
mask = body.getchannel('A')
mask = mask.crop(mask.getbbox()).resize((620, 290), Image.Resampling.LANCZOS)
canvas = Image.new('RGBA', (1024, 1024))
for layer in (mask.rotate(-45, Image.Resampling.BICUBIC, expand=True),
              ImageOps.mirror(mask).rotate(45, Image.Resampling.BICUBIC, expand=True)):
    outline = layer.filter(ImageFilter.MaxFilter(25))
    tile = Image.new('RGBA', layer.size, (64, 64, 64, 0))
    tile.putalpha(outline)
    white = Image.new('RGBA', layer.size, (255, 255, 255, 0))
    white.putalpha(layer)
    tile.alpha_composite(white)
    canvas.alpha_composite(tile, ((1024-tile.width)//2, (1024-tile.height)//2))
canvas.convert('RGBa').resize((512, 512), Image.Resampling.LANCZOS).convert('RGBA').save(
    TEX / 'Boltshot/Boltshot_DualGrip_Icon.png')
print('Prepared 1024-pixel rifle layers and unchanged 512-pixel pistol layers.')
