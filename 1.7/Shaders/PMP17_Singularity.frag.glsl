#version 450
layout(set=2,binding=0) uniform MaterialUniforms {
    // absorption, closure, total contraction progress, field radius
    vec4 _State; // @default = vec4(0.0, 0.0, 0.0, 0.8)
} u;
layout(set=2,binding=1) uniform sampler2D _MainTex;
layout(location=0) in vec4 vColor;
layout(location=1) in vec2 vUv;
layout(location=0) out vec4 outColor;
float hash(float n) { return fract(sin(n*127.1+311.7)*43758.5453); }
// Unequal angular partitions, stable over time and across the 0/2pi seam.
// Keeping the boundaries irregular (not just the alpha widths) avoids a turbine pattern.
vec2 ribbon(float turn) {
    const float cuts[15]=float[15](0.,.042,.128,.173,.263,.326,.373,.472,
                                   .542,.589,.689,.747,.815,.925,1.);
    float a=fract(turn);
    for(int i=0;i<14;i++) {
        if(a<cuts[i+1]) return vec2((a-cuts[i])/(cuts[i+1]-cuts[i]),hash(float(i)));
    }
    return vec2(1.,hash(13.));
}
void main() {
    const float TAU=6.2831853;
    float p=clamp(u._State.x,0.,1.);
    // One linear envelope spans BOTH absorption and closure. Switching stages cannot
    // restart the scale or introduce a last-second acceleration.
    float collapse=1.-clamp(u._State.z,0.,1.);
    vec2 q=(vUv*2.-1.)/max(.0001,collapse);
    float r=length(q),angle=atan(q.y,q.x);
    float shear=p*p*(3.-2.*p);
    float fieldRadius=max(.1,u._State.w);
    float relativeRadius=r/fieldRadius;
    // The outer skin initially stays attached to the surrounding image. Tension propagates
    // outwards, and that skin releases progressively instead of exposing a cookie-cutter rim.
    float attachment=smoothstep(.60,.995,relativeRadius)*(1.-smoothstep(.55,.98,p));
    // Newtonian radial free fall: dr/dt=-sqrt(2GM/r), hence
    // r0=(r^(3/2)+(3/2)*sqrt(2GM)*t)^(2/3). Dimensionless time is art-directed.
    // Its deformation stretches radially and compresses transversely; the underlying
    // 3D tidal eigenvalues are (2,-1,-1)*GM/r^3. This is NOT a GR ray tracer.
    // Normalize the advected radius at the outer boundary: tidal deformation must not
    // add a second, nonlinear shrink on top of the linear envelope. Fade the correction
    // outside that boundary for the extra footprint of straddling buildings.
    float sourceRho=pow((pow(relativeRadius,1.5)+.58*shear)/(1.+.58*shear),2./3.);
    sourceRho=mix(sourceRho,relativeRadius,smoothstep(1.,1.35,relativeRadius));
    // Low angular momentum perturbation: dphi/dt=L/r^2 integrated along radial fall.
    // Soften the central singularity; no rigid rotation of the entire captured disk.
    float winding=2.15*(inversesqrt(relativeRadius+.075)-inversesqrt(sourceRho+.075))*(1.-attachment);
    float sourceAngle=angle+winding;
    float tearAngle=sourceAngle+shear*.055*sin(sourceAngle*3.+sourceRho*4.);
    vec2 strip=ribbon((tearAngle+3.14159265)/TAU);
    float lane=strip.x,seed=strip.y;
    float width=mix(1.,.48+seed*.22,smoothstep(.03,.8,p));
    float edgeDistance=abs(lane-.5);
    float aa=min(.07,max(.006,fwidth(lane)));
    float gap=1.-smoothstep(width*.5-aa,width*.5+aa,edgeDistance);
    float bridges=smoothstep(.72,.995,relativeRadius)*(1.-smoothstep(.30,.88,p));
    gap=mix(gap,1.,bridges);
    // Preserve stretched remnants until closure; the outer skin releases gradually.
    float sourceRadius=mix(sourceRho*fieldRadius,r,attachment);
    vec2 source=vec2(cos(sourceAngle),sin(sourceAngle))*sourceRadius;
    vec2 uv=source*.5+.5;
    vec4 sampleColor=vec4(0.);
    if(all(greaterThanEqual(uv,vec2(0.)))&&all(lessThanEqual(uv,vec2(1.))))
        sampleColor=texture(_MainTex,vec2(uv.x,1.-uv.y));
    sampleColor.a*=mix(1.,gap,smoothstep(.015,.14,p));
    float holeRadius=.012+.045*smoothstep(0.,.3,p);
    float hole=(1.-smoothstep(holeRadius*.8,holeRadius,r))*smoothstep(0.,.06,p);
    float edge=exp(-pow((r-holeRadius*1.12)/.003,2.))*smoothstep(0.,.1,p);
    vec3 color=mix(sampleColor.rgb,vec3(.002),hole);
    // Only the narrow horizon emits. No tinted disk or additive background wash.
    color=mix(color,vec3(.77,.86,1.),edge*.7);
    float alpha=max(sampleColor.a,max(hole,edge*.6));
    if(alpha<.002||collapse<.001)discard;
    outColor=vec4(color,clamp(alpha,0.,1.));
}
