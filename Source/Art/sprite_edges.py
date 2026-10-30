"""Deterministic alpha cleanup; pawn outlines and modular-gun layers are separate policies."""
from collections import deque
import numpy as np
from PIL import Image, ImageFilter, ImageChops


def clean_cutout(image, trim=0):
    """Discard disconnected alpha specks and the narrow baked matte fringe.

    Use on one connected sprite/part, before composition. Keep its canvas and anchor.
    Transparent pixels are zeroed so texture filtering cannot sample colored matte.
    """
    rgba=np.array(image.convert('RGBA'))
    mask=rgba[:,:,3]>=128
    seen=np.zeros(mask.shape,dtype=bool)
    largest=[]
    height,width=mask.shape
    for y,x in zip(*np.nonzero(mask)):
        if seen[y,x]:continue
        queue=deque([(y,x)]);seen[y,x]=True;component=[]
        while queue:
            cy,cx=queue.popleft();component.append((cy,cx))
            for dy,dx in ((-1,0),(1,0),(0,-1),(0,1),(-1,-1),(-1,1),(1,-1),(1,1)):
                ny,nx=cy+dy,cx+dx
                if 0<=ny<height and 0<=nx<width and mask[ny,nx] and not seen[ny,nx]:
                    seen[ny,nx]=True;queue.append((ny,nx))
        if len(component)>len(largest):largest=component
    silhouette=np.zeros(mask.shape,dtype=np.uint8)
    if largest:
        yy,xx=zip(*largest);silhouette[yy,xx]=255
    alpha=Image.fromarray(silhouette)
    if trim:alpha=alpha.filter(ImageFilter.MinFilter(trim*2+1))
    rgba[:,:,3]=np.array(alpha)
    rgba[rgba[:,:,3]==0]=0
    result=Image.fromarray(rgba)
    # Trimming can detach a one-pixel spur from the former edge. Remove those too.
    return clean_cutout(result) if trim else result


def pawn_outline(image,radius=3):
    """Replace exterior fringe with solid black, then add a circular black border."""
    clean=clean_cutout(image)
    rgba=np.array(clean)
    mask=clean.getchannel('A')
    inside=mask.filter(ImageFilter.MinFilter(5))
    edge=(np.array(mask)>0)&(np.array(inside)==0)
    rgba[edge,:3]=0
    # Circular dilation, with black RGB even in the antialias-free silhouette edge.
    padded=Image.new('L',(image.width+radius*2,image.height+radius*2))
    padded.paste(mask,(radius,radius))
    expanded=Image.new('L',image.size)
    for dy in range(-radius,radius+1):
        for dx in range(-radius,radius+1):
            if dx*dx+dy*dy<=radius*radius:
                shifted=padded.crop((radius-dx,radius-dy,radius-dx+image.width,radius-dy+image.height))
                expanded=ImageChops.lighter(expanded,shifted)
    result=Image.new('RGBA',image.size,(0,0,0,0));result.putalpha(expanded)
    result.alpha_composite(Image.fromarray(rgba))
    return result
