#version 450
layout(set=2,binding=0) uniform MaterialUniforms {
    vec4 _CameraLocal; // @default = vec4(0.0, 5.0, 0.0, 1.0)
    vec4 _RayDirection; // @default = vec4(0.0, -1.0, 0.0, 0.0)
    vec4 _Beam; // @default = vec4(0.0, 0.0, 1.0, 0.0)
} u;
layout(location=0) in vec3 localPosition;
layout(location=1) flat in vec3 faceNormal;
layout(location=0) out vec4 outColor;
void main() {
    vec3 rd=u._CameraLocal.w>.5 ? normalize(u._RayDirection.xyz) : normalize(localPosition-u._CameraLocal.xyz);
    if(dot(faceNormal,rd)>=0.) discard;
    vec3 ro=localPosition-rd*.002;
    vec3 inverse=1./(rd+vec3(1e-7));
    vec3 a=(-vec3(1)-ro)*inverse,b=(vec3(1)-ro)*inverse;
    vec3 lo=min(a,b),hi=max(a,b);
    float entry=max(max(lo.x,lo.y),lo.z),exit=min(min(hi.x,hi.y),hi.z);
    entry=max(entry,0.); if(exit<=entry) discard;
    float stepLength=(exit-entry)/56.,opacity=0.;vec3 emission=vec3(0.);
    for(int i=0;i<56;i++) {
        vec3 p=ro+rd*(entry+(float(i)+.5)*stepLength);
        if(u._Beam.y>.5 && p.y<1.-2.*clamp(u._Beam.x/(8./60.),0.,1.)) continue;
        float radial=length(p.xz);
        float core=exp(-radial*radial/(u._Beam.y>.5 ? .20 : .008));
        float yRing=(p.y+.98)*6.;
        float torus=length(vec2(radial-.64,yRing));
        float ring=exp(-torus*torus/.0012)*(1.-u._Beam.y*.7);
        float pulse=.85+.15*sin(p.y*70.-u._Beam.x*40.);
        float edge=1.-smoothstep(.72,.98,radial);
        // A thick column needs lower absorption: otherwise its amber front surface
        // becomes opaque before the ray reaches the white emissive core.
        float density=(core*(u._Beam.y>.5 ? .75 : 5.)+ring*8.)*pulse*u._Beam.z*edge;
        float alpha=1.-exp(-density*stepLength*7.);
        float whiteCore=u._Beam.y>.5 ? 1.-smoothstep(.10,.50,radial) : core;
        vec3 tint=mix(vec3(1.,.65,.08),vec3(1.,.99,.85),whiteCore);
        emission+=(1.-opacity)*tint*alpha;opacity+=(1.-opacity)*alpha;
        if(opacity>.995)break;
    }
    if(opacity<.002)discard;
    outColor=vec4(emission/max(opacity,.001),opacity);
}
