"""Finish ImageGen sprites with circular black borders and supersampled alpha edges."""
from pathlib import Path
import shutil
import numpy as np
from PIL import Image
from sprite_edges import pawn_outline

ROOT = Path(__file__).resolve().parents[2]
GENERATED = Path(r'C:\Users\15858\.codex\generated_images\01a175ee-268a-7aa0-9016-f515a99cc55a')
SOURCES = {
    'FleetBeacon': 'exec-4e9f94f2-1aed-4953-8e82-1fd1c646f606.png',
    'PurifierConclave': 'exec-85ee3c07-2262-4f9c-9264-af258faab9f1.png',
}
ART = ROOT / 'artifacts/art/protoss-buildings'
ART.mkdir(parents=True, exist_ok=True)
for name, file in SOURCES.items():
    raw = ART / (name + '-simplified-imagegen.png')
    if not raw.exists():
        shutil.copy2(GENERATED / file, raw)
    target = ROOT / 'Textures/Things/Structure' / (name + '.png')
    backup = ART / (name + '-previous.png')
    if not backup.exists():
        shutil.copy2(target, backup)
    # Work at 2x output resolution, then use premultiplied-alpha Lanczos filtering.
    source = Image.open(raw).convert('RGBA')
    assert source.getchannel('A').getextrema() == (0, 255), 'Expected real transparency'
    sprite = source.crop(source.getchannel('A').getbbox())
    sprite.thumbnail((1840, 1840), Image.Resampling.LANCZOS)
    # thumbnail does not upscale; explicitly fit to the shared building canvas.
    scale = 1840 / max(sprite.size)
    sprite = sprite.resize(tuple(round(v * scale) for v in sprite.size), Image.Resampling.LANCZOS)
    canvas = Image.new('RGBA', (2048, 2048))
    canvas.alpha_composite(sprite, ((2048-sprite.width)//2, (2048-sprite.height)//2))
    result = pawn_outline(canvas, radius=6).convert('RGBa').resize((1024, 1024), Image.Resampling.LANCZOS).convert('RGBA')
    pixels = np.array(result)
    pixels[pixels[:, :, 3] == 0] = 0
    result = Image.fromarray(pixels)
    result.save(target)
    assert np.any((pixels[:, :, 3] > 0) & (pixels[:, :, 3] < 255)), 'Missing antialiasing'
    print(name, result.size, 'black outline + antialiased transparent edges')
